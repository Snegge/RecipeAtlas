using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Data.Common;
using Recipoke.Api.Common.Application;
using Recipoke.Api.Features.Shopping.Application;
using Recipoke.Domain.Shopping;

internal static class ShoppingTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "recipoke-shopping-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=" + Path.Combine(directory, "test.db")).Options;
        var owner = Guid.NewGuid(); var member = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var clock = new ShoppingClock();
        var transactions = new ShoppingTransactionGuard();
        async Task<T> Run<T>(Guid actor, Func<ShoppingOperations, Task<T>> action)
        {
            await using var db = new RecipeDb(new DbContextOptionsBuilder<RecipeDb>(options).AddInterceptors(transactions).Options);
            var accounts = new AccountAccess(db, new TestCurrentUser(actor));
            return await action(new ShoppingOperations(db, accounts, new ShoppingAccess(db, accounts), new UpperInvariantLookupNormalizer(), clock,
                new FailedNotifications(), NullLogger<ShoppingOperations>.Instance));
        }
        static async Task<T[]> Race<T>(Func<Task<T>> first, Func<Task<T>> second)
        {
            using var start = new Barrier(2);
            return await Task.WhenAll(Task.Run(async () => { start.SignalAndWait(); return await first(); }),
                Task.Run(async () => { start.SignalAndWait(); return await second(); }));
        }
        async Task Fail(Guid actor, string code, Func<ShoppingOperations, Task> action)
        {
            try { await Run(actor, async operations => { await action(operations); return true; }); throw new Exception("Shopping operation unexpectedly succeeded: " + code); }
            catch (OperationFailure error) { check(error.Code == code, "Shopping failure " + code + " (actual " + error.Code + ")"); }
        }
        try
        {
            await using (var db = new RecipeDb(options))
            {
                await db.Database.MigrateAsync();
                foreach (var (id, name) in new[] { (owner, "owner"), (member, "member"), (outsider, "admin") })
                    db.Users.Add(new() { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), DisplayName = name, IsActive = true, IsAdministrator = id == outsider });
                await db.SaveChangesAsync();
            }
            var listId = Guid.NewGuid(); var create = new CreateShoppingListInput(listId, " Weekly ", Guid.NewGuid());
            var created = await Run(owner, x => x.CreateAsync(create));
            check(created.Version.HasValue, "Shopping create returns administrative version despite notification failure");
            check(await Run(owner, x => x.CreateAsync(create)) == created, "Create replay after a new context returns its historical receipt");
            await Fail(owner, "operation_id_reused", x => x.CreateAsync(create with { Name = "Other" }));
            await Fail(outsider, "resource_unavailable", x => x.GetAsync(listId));
            var invited = await Run(owner, x => x.InviteAsync(listId, new("member", created.Version.ToString(), Guid.NewGuid())));
            var inbox = await Run(member, x => x.InboxAsync());
            check(inbox.Count == 1 && inbox[0].ListId == listId && inbox[0].ListVersion == invited.Version, "Invitation inbox exposes accepted-account invitation and administrative version");
            await Fail(member, "resource_unavailable", x => x.GetAsync(listId));
            await Run(member, x => x.RespondAsync(listId, inbox[0].Id, new(true, invited.Version.ToString(), Guid.NewGuid())));
            var list = await Run(member, x => x.GetAsync(listId));
            check(list.Role == "Member" && list.Name == "Weekly" && list.Members.Count == 2 && list.Invitations.Count == 0, "Accepted members receive coherent list, member roles, and no owner inbox");

            var itemId = Guid.NewGuid(); var add = new AddShoppingItemInput(itemId, " Milk ", Guid.NewGuid());
            var added = await Run(member, x => x.AddItemAsync(listId, add));
            check(added.Item!.Text == "Milk" && added.Item.Position == 0, "Item addition trims text and assigns stable insertion position");
            check(await Run(member, x => x.AddItemAsync(listId, add)) == added, "Add retries retain the original item result");
            await Fail(member, "operation_id_reused", x => x.AddItemAsync(listId, add with { Text = "Bread" }));
            var token = Guid.NewGuid();
            var lease = await Run(owner, x => x.AcquireAsync(listId, itemId, new(token, added.Item.Version.ToString())));
            check(lease.ExpiresAtUtc == clock.GetUtcNow().AddSeconds(15), "Editing protection lasts fifteen seconds");
            await Fail(member, "shopping_item_reserved", x => x.AcquireAsync(listId, itemId, new(Guid.NewGuid(), added.Item.Version.ToString())));
            await Fail(owner, "shopping_item_reserved", x => x.SetPurchasedAsync(listId, itemId, new(true, added.Item.Version.ToString(), Guid.NewGuid())));
            await Fail(member, "shopping_item_reserved", x => x.DeleteItemAsync(listId, itemId, new(added.Item.Version.ToString()), Guid.NewGuid()));
            var staleSave = new SaveShoppingItemInput("Rejected text", Guid.NewGuid().ToString(), token, Guid.NewGuid());
            await Fail(owner, "concurrency_conflict", x => x.SaveItemAsync(listId, itemId, staleSave));
            await using (var db = new RecipeDb(options))
                check((await db.ShoppingItems.SingleAsync(x => x.Id == itemId)).Text == "Milk" &&
                    await db.ShoppingReservations.AnyAsync(x => x.ItemId == itemId && x.Token == token) &&
                    !await db.ShoppingReceipts.AnyAsync(x => x.OperationId == staleSave.OperationId), "Stale text CAS rolls back reservation release, text and receipt");
            var save = new SaveShoppingItemInput("Oat milk", added.Item.Version.ToString(), token, Guid.NewGuid());
            var saved = await Run(owner, x => x.SaveItemAsync(listId, itemId, save));
            check(saved.Item!.Text == "Oat milk" && saved.Item.Version != added.Item.Version, "Text save changes version and releases protection atomically");
            check(await Run(owner, x => x.SaveItemAsync(listId, itemId, save)) == saved, "Lost save response replays receipt after the reservation was released");
            await Fail(owner, "shopping_reservation_lost", x => x.AcquireAsync(listId, itemId, new(token, saved.Item.Version.ToString())));
            var purchased = await Run(member, x => x.SetPurchasedAsync(listId, itemId, new(true, saved.Item.Version.ToString(), Guid.NewGuid())));
            var samePurchased = await Run(owner, x => x.SetPurchasedAsync(listId, itemId, new(true, saved.Item.Version.ToString(), Guid.NewGuid())));
            check(samePurchased.Item!.Version == purchased.Item!.Version, "Same desired checkbox value is an idempotent no-op with a stale valid version");
            await Fail(owner, "concurrency_conflict", x => x.SetPurchasedAsync(listId, itemId, new(false, saved.Item.Version.ToString(), Guid.NewGuid())));

            var deletion = Guid.NewGuid();
            var removed = await Run(member, x => x.DeleteItemAsync(listId, itemId, new(purchased.Item.Version.ToString()), deletion));
            check(removed.UndoUntilUtc == clock.GetUtcNow().AddSeconds(5), "Deletion starts one five-second Undo window");
            await Fail(owner, "shopping_permission_required", x => x.RestoreItemAsync(listId, itemId, new(deletion, Guid.NewGuid())));
            clock.Advance(2);
            var restored = await Run(member, x => x.RestoreItemAsync(listId, itemId, new(deletion, Guid.NewGuid())));
            check(restored.Item!.Id == itemId && restored.Item.Purchased && restored.Item.Text == "Oat milk", "Same actor Undo restores item identity and complete contents");
            check(await Run(member, x => x.DeleteItemAsync(listId, itemId, new(purchased.Item.Version.ToString()), deletion)) == removed, "Old deletion replay after Undo returns receipt without deleting again");
            var deletion2 = Guid.NewGuid();
            await Run(member, x => x.DeleteItemAsync(listId, itemId, new(restored.Item.Version.ToString()), deletion2));
            await Fail(member, "shopping_undo_expired", x => x.RestoreItemAsync(listId, itemId, new(deletion, Guid.NewGuid())));
            clock.Advance(5);
            await Fail(member, "shopping_undo_expired", x => x.RestoreItemAsync(listId, itemId, new(deletion2, Guid.NewGuid())));
            await Run(owner, x => x.AddItemAsync(listId, new(Guid.NewGuid(), "Bread", Guid.NewGuid())));
            await Fail(member, "shopping_id_reused", x => x.AddItemAsync(listId, add with { OperationId = Guid.NewGuid() }));
            await using (var db = new RecipeDb(options))
            {
                var erased = await db.ShoppingItems.SingleAsync(x => x.Id == itemId);
                check(erased.Text == "" && erased.UndoUntilUtc == null, "Expired Undo recovery is purged while consumed item identity remains");
            }

            var next = (await Run(owner, x => x.GetAsync(listId))).Items.Single();
            var oldToken = Guid.NewGuid();
            await Run(owner, x => x.AcquireAsync(listId, next.Id, new(oldToken, next.Version.ToString())));
            clock.Advance(15);
            await Fail(owner, "shopping_reservation_lost", x => x.AcquireAsync(listId, next.Id, new(oldToken, next.Version.ToString())));
            var newToken = Guid.NewGuid();
            await Run(owner, x => x.AcquireAsync(listId, next.Id, new(newToken, next.Version.ToString())));
            await Fail(owner, "shopping_reservation_lost", x => x.ReleaseAsync(listId, next.Id, oldToken));
            await Fail(owner, "shopping_reservation_lost", x => x.SaveItemAsync(listId, next.Id, new("late", next.Version.ToString(), oldToken, Guid.NewGuid())));
            await Run(owner, async x => { await x.ReleaseAsync(listId, next.Id, newToken); return true; });
            await Fail(owner, "shopping_reservation_lost", x => x.AcquireAsync(listId, next.Id, new(oldToken, next.Version.ToString())));

            list = await Run(owner, x => x.GetAsync(listId));
            var rename = new RenameShoppingListInput("Renamed", list.Version.ToString(), Guid.NewGuid());
            var renamed = await Run(owner, x => x.RenameAsync(listId, rename));
            list = await Run(owner, x => x.GetAsync(listId));
            var transfer = await Run(owner, x => x.TransferAsync(listId, new(member, list.Version.ToString(), Guid.NewGuid())));
            check((await Run(owner, x => x.GetAsync(listId))).Role == "Member", "Transfer makes the former owner a member");
            await Fail(owner, "shopping_owner_required", x => x.RenameAsync(listId, rename));
            await Fail(owner, "shopping_owner_required", x => x.RenameAsync(listId, new("Denied", transfer.Version.ToString(), Guid.NewGuid())));
            await Run(owner, x => x.LeaveAsync(listId, new(transfer.Version.ToString(), Guid.NewGuid())));
            await Fail(owner, "resource_unavailable", x => x.SaveItemAsync(listId, itemId, save));
            await using (var db = new RecipeDb(options)) { (await db.Users.SingleAsync(x => x.Id == member)).IsActive = false; await db.SaveChangesAsync(); }
            await Fail(member, "authentication_required", x => x.AddItemAsync(listId, add));
            await using (var db = new RecipeDb(options)) { (await db.Users.SingleAsync(x => x.Id == member)).IsActive = true; await db.SaveChangesAsync(); }
            list = await Run(member, x => x.GetAsync(listId));
            await Run(member, async x => { await x.DeleteAsync(listId, new(list.Version.ToString()), Guid.NewGuid()); return true; });
            await Fail(member, "resource_unavailable", x => x.CreateAsync(create));
            await Fail(owner, "resource_unavailable", x => x.CreateAsync(create));
            await using (var db = new RecipeDb(options))
                check(!await db.ShoppingReceipts.AnyAsync() && !await db.ShoppingItems.AnyAsync() && !await db.ShoppingMemberships.AnyAsync(), "Whole-list deletion removes all content, receipts and memberships");

            // Every competitor uses its own real SQLite connection; simultaneous retries commit one receipt/effect.
            var concurrentList = Guid.NewGuid();
            var concurrentCreate = new CreateShoppingListInput(concurrentList, "Races", Guid.NewGuid());
            var creates = await Race(() => Run(owner, x => x.CreateAsync(concurrentCreate)), () => Run(owner, x => x.CreateAsync(concurrentCreate)));
            check(creates[0] == creates[1], "Concurrent creation retries converge to one successful receipt");
            var concurrentItem = new AddShoppingItemInput(Guid.NewGuid(), "One item", Guid.NewGuid());
            var adds = await Race(() => Run(owner, x => x.AddItemAsync(concurrentList, concurrentItem)), () => Run(owner, x => x.AddItemAsync(concurrentList, concurrentItem)));
            check(adds[0] == adds[1] && (await Run(owner, x => x.GetAsync(concurrentList))).Items.Count == 1, "Concurrent add retries create exactly one item");
            async Task<(ShoppingReservationResponse? Lease, OperationFailure? Error)> Reserve(Guid candidate)
            {
                try { return (await Run(owner, x => x.AcquireAsync(concurrentList, concurrentItem.Id, new(candidate, adds[0].Item!.Version.ToString()))), null); }
                catch (OperationFailure error) { return (null, error); }
            }
            var reservations = await Race(() => Reserve(Guid.NewGuid()), () => Reserve(Guid.NewGuid()));
            check(reservations.Count(x => x.Lease != null) == 1 && reservations.Single(x => x.Error != null).Error!.Code == "shopping_item_reserved", "Concurrent sessions of the same actor cannot share an edit reservation");
            var winnerToken = reservations.Single(x => x.Lease != null).Lease!.Token;
            var originalExpiry = reservations.Single(x => x.Lease != null).Lease!.ExpiresAtUtc;
            clock.Advance(5);
            var sameSession = await Run(owner, x => x.AcquireAsync(concurrentList, concurrentItem.Id, new(winnerToken, adds[0].Item!.Version.ToString())));
            check(sameSession.ExpiresAtUtc == originalExpiry && sameSession.ExpiresAtUtc == clock.GetUtcNow().AddSeconds(10), "Repeated acquisition retains the original fifteen-second deadline without extension");
            var secondItem = await Run(owner, x => x.AddItemAsync(concurrentList, new(Guid.NewGuid(), "Independent", Guid.NewGuid())));
            var independent = await Run(owner, x => x.SetPurchasedAsync(concurrentList, secondItem.Item!.Id, new(true, secondItem.Item.Version.ToString(), Guid.NewGuid())));
            check(independent.Item!.Purchased && (await Run(owner, x => x.GetAsync(concurrentList))).Version == creates[0].Version, "Independent item writes ignore other-item reservations and retain administrative version");
            await Fail(owner, "resource_unavailable", x => x.SetPurchasedAsync(listId, secondItem.Item!.Id, new(false, secondItem.Item.Version.ToString(), Guid.NewGuid())));

            // Failure after a real statement must roll back content, released reservation, and successful receipt together.
            var injected = new ShoppingWriteFailure();
            var failingOptions = new DbContextOptionsBuilder<RecipeDb>().UseSqlite("Data Source=" + Path.Combine(directory, "test.db")).AddInterceptors(injected).Options;
            var failedSave = new SaveShoppingItemInput("Must not persist", adds[0].Item!.Version.ToString(), winnerToken, Guid.NewGuid());
            await using (var db = new RecipeDb(failingOptions))
            {
                var accounts = new AccountAccess(db, new TestCurrentUser(owner));
                var operation = new ShoppingOperations(db, accounts, new ShoppingAccess(db, accounts), new UpperInvariantLookupNormalizer(), clock, new FailedNotifications(), NullLogger<ShoppingOperations>.Instance);
                try { await operation.SaveItemAsync(concurrentList, concurrentItem.Id, failedSave); throw new Exception("Injected shopping write failure was not reached"); }
                catch (DbUpdateException) { check(injected.Writes > 0, "Shopping rollback is exercised after an actual SQLite write"); }
            }
            await using (var db = new RecipeDb(options))
            {
                check((await db.ShoppingItems.SingleAsync(x => x.Id == concurrentItem.Id)).Text == "One item" &&
                    await db.ShoppingReservations.AnyAsync(x => x.ItemId == concurrentItem.Id && x.Token == winnerToken) &&
                    !await db.ShoppingReceipts.AnyAsync(x => x.OperationId == failedSave.OperationId), "Rollback restores original text, edit reservation, and absence of receipt");
            }
            var successfulRetry = await Run(owner, x => x.SaveItemAsync(concurrentList, concurrentItem.Id, failedSave));
            check(successfulRetry.Item!.Text == "Must not persist", "A rolled-back operation ID remains available for the original immutable retry");

            async Task<string> Compete(Func<ShoppingOperations, Task> action)
            {
                try { await Run(owner, async x => { await action(x); return true; }); return "success"; }
                catch (OperationFailure error) { return error.Code; }
            }
            var itemVersion = successfulRetry.Item!.Version.ToString();
            var contentRace = await Race(
                () => Compete(x => x.SetPurchasedAsync(concurrentList, concurrentItem.Id, new(true, itemVersion, Guid.NewGuid()))),
                () => Compete(x => x.DeleteItemAsync(concurrentList, concurrentItem.Id, new(itemVersion), Guid.NewGuid())));
            check(contentRace.Count(x => x == "success") == 1 && contentRace.All(x => x is "success" or "concurrency_conflict" or "resource_unavailable"), "Competing checkbox/removal commits one reviewed item version");
            var editRaceItem = await Run(owner, x => x.AddItemAsync(concurrentList, new(Guid.NewGuid(), "Editing", Guid.NewGuid())));
            var editRaceToken = Guid.NewGuid();
            await Run(owner, x => x.AcquireAsync(concurrentList, editRaceItem.Item!.Id, new(editRaceToken, editRaceItem.Item.Version.ToString())));
            var editRace = await Race(
                () => Compete(x => x.SaveItemAsync(concurrentList, editRaceItem.Item!.Id, new("Saved", editRaceItem.Item.Version.ToString(), editRaceToken, Guid.NewGuid()))),
                () => Compete(x => x.DeleteItemAsync(concurrentList, editRaceItem.Item!.Id, new(editRaceItem.Item.Version.ToString()), Guid.NewGuid())));
            check(editRace[0] == "success" && editRace[1] is "shopping_item_reserved" or "concurrency_conflict", "Concurrent delete cannot remove a reserved item or silently delete its newly saved version");
            var adminRace = await Race(
                () => Compete(x => x.RenameAsync(concurrentList, new("Winner", creates[0].Version.ToString(), Guid.NewGuid()))),
                () => Compete(x => x.InviteAsync(concurrentList, new("member", creates[0].Version.ToString(), Guid.NewGuid()))));
            check(adminRace.Count(x => x == "success") == 1 && adminRace.Count(x => x == "concurrency_conflict") == 1, "Invitation and rename compete atomically on administrative version");
            var adminState = await Run(owner, x => x.GetAsync(concurrentList));
            check((adminState.Name == "Winner" && adminState.Invitations.Count == 0) || (adminState.Name == "Races" && adminState.Invitations.Count == 1), "Losing administrative CAS rolls back pending invitation changes");

            var accessList = Guid.NewGuid();
            var accessCreated = await Run(owner, x => x.CreateAsync(new(accessList, "Access rules", Guid.NewGuid())));
            var inviteVersion = await Run(owner, x => x.InviteAsync(accessList, new("member", accessCreated.Version.ToString(), Guid.NewGuid())));
            var pending = (await Run(member, x => x.InboxAsync())).Single(x => x.ListId == accessList);
            await Fail(outsider, "resource_unavailable", x => x.RespondAsync(accessList, pending.Id, new(true, inviteVersion.Version.ToString(), Guid.NewGuid())));
            var cancelId = Guid.NewGuid();
            var canceled = await Run(owner, x => x.CancelInvitationAsync(accessList, pending.Id, new(inviteVersion.Version.ToString()), cancelId));
            check(await Run(owner, x => x.CancelInvitationAsync(accessList, pending.Id, new(inviteVersion.Version.ToString()), cancelId)) == canceled &&
                !(await Run(member, x => x.InboxAsync())).Any(x => x.ListId == accessList), "Canceled invitation replay is stable and removes its recipient inbox entry");
            var reInvited = await Run(owner, x => x.InviteAsync(accessList, new("member", canceled.Version.ToString(), Guid.NewGuid())));
            pending = (await Run(member, x => x.InboxAsync())).Single(x => x.ListId == accessList);
            var declined = await Run(member, x => x.RespondAsync(accessList, pending.Id, new(false, reInvited.Version.ToString(), Guid.NewGuid())));
            check(declined.Version is null && declined.Item is null && declined.UndoUntilUtc is null, "Invitation decline returns no protected list state");
            await Fail(member, "resource_unavailable", x => x.GetAsync(accessList));
            var accessCurrent = await Run(owner, x => x.GetAsync(accessList));
            await Run(owner, x => x.InviteAsync(accessList, new("member", accessCurrent.Version.ToString(), Guid.NewGuid())));
            pending = (await Run(member, x => x.InboxAsync())).Single(x => x.ListId == accessList);
            var accepted = await Run(member, x => x.RespondAsync(accessList, pending.Id, new(true, pending.ListVersion.ToString(), Guid.NewGuid())));
            var sharedItem = await Run(member, x => x.AddItemAsync(accessList, new(Guid.NewGuid(), "Shared", Guid.NewGuid())));
            var memberToken = Guid.NewGuid();
            await Run(member, x => x.AcquireAsync(accessList, sharedItem.Item!.Id, new(memberToken, sharedItem.Item.Version.ToString())));
            var selfRemoved = await Run(member, x => x.RemoveMemberAsync(accessList, member, new(accepted.Version.ToString()), Guid.NewGuid()));
            check(selfRemoved.Version is null && selfRemoved.Item is null, "Self-removal returns no protected version or item");
            await Fail(member, "resource_unavailable", x => x.AcquireAsync(accessList, sharedItem.Item!.Id, new(memberToken, sharedItem.Item.Version.ToString())));
            var ownerLease = await Run(owner, x => x.AcquireAsync(accessList, sharedItem.Item!.Id, new(Guid.NewGuid(), sharedItem.Item.Version.ToString())));
            check(ownerLease.ExpiresAtUtc > clock.GetUtcNow(), "Member removal releases the removed actor's edit reservation");

            var boundedList = Guid.NewGuid();
            var bounded = await Run(owner, x => x.CreateAsync(new(boundedList, "Limits", Guid.NewGuid())));
            await Fail(owner, "invalid_shopping_input", x => x.AddItemAsync(boundedList, new(Guid.NewGuid(), " ", Guid.NewGuid())));
            await Fail(owner, "invalid_shopping_input", x => x.AddItemAsync(boundedList, new(Guid.NewGuid(), new string('x', 501), Guid.NewGuid())));
            await Fail(owner, "invalid_shopping_input", x => x.RenameAsync(boundedList, new(new string('x', 101), bounded.Version.ToString(), Guid.NewGuid())));
            await Fail(owner, "invalid_shopping_input", x => x.AddItemAsync(boundedList, new(Guid.Empty, "Milk", Guid.NewGuid())));
            await Fail(owner, "invalid_shopping_input", x => x.AddItemAsync(boundedList, new(Guid.NewGuid(), "Milk", Guid.Empty)));
            await Fail(owner, "concurrency_precondition_required", x => x.RenameAsync(boundedList, new("No baseline", null, Guid.NewGuid())));
            await Fail(owner, "invalid_concurrency_precondition", x => x.RenameAsync(boundedList, new("Bad baseline", "*", Guid.NewGuid())));
            await using (var db = new RecipeDb(options))
            {
                check(await db.ShoppingReceipts.CountAsync(x => x.ListId == boundedList) == 1, "Failed validation and preconditions create no successful receipts");
                db.ShoppingItems.AddRange(Enumerable.Range(0, 1000).Select(i => new ShoppingItem { Id = Guid.NewGuid(), ListId = boundedList, Text = "Item " + i, Position = i }));
                await db.SaveChangesAsync();
            }
            var overflow = new AddShoppingItemInput(Guid.NewGuid(), "Overflow", Guid.NewGuid());
            await Fail(owner, "shopping_item_limit", x => x.AddItemAsync(boundedList, overflow));
            var full = await Run(owner, x => x.GetAsync(boundedList));
            await Run(owner, x => x.DeleteItemAsync(boundedList, full.Items[0].Id, new(full.Items[0].Version.ToString()), Guid.NewGuid()));
            await Fail(owner, "shopping_item_limit", x => x.AddItemAsync(boundedList, overflow));
            clock.Advance(5);
            var afterPurge = await Run(owner, x => x.AddItemAsync(boundedList, overflow));
            check(afterPurge.Item!.Position == 1000 && (await Run(owner, x => x.GetAsync(boundedList))).Items.Count == 1000, "Expired recovery releases capacity without reusing insertion positions");
            var summaries = await Run(owner, x => x.ListAsync());
            check(summaries.Single(x => x.Id == boundedList).Preview.SequenceEqual(new[] { "Item 1", "Item 2", "Item 3", "Item 4" }) &&
                summaries.Single(x => x.Id == boundedList).ActiveCount == 1000, "Overview previews only the first four unpurchased items with accurate remaining count");
            await Run(owner, x => x.AcquireAsync(boundedList, afterPurge.Item.Id, new(Guid.NewGuid(), afterPurge.Item.Version.ToString())));
            await Run(owner, async x => { await x.DeleteAsync(boundedList, new(bounded.Version.ToString()), Guid.NewGuid()); return true; });
            await Fail(owner, "resource_unavailable", x => x.GetAsync(boundedList));
            check(transactions.Reads > 100, "All exercised shopping account/access reads occur after the operation's transaction begins");
            await using (var db = new RecipeDb(options))
            {
                var previousMigration = (await db.Database.GetAppliedMigrationsAsync()).Reverse().Skip(1).First();
                try { await db.GetService<IMigrator>().MigrateAsync(previousMigration); throw new Exception("Unsafe shopping downgrade was allowed"); }
                catch (NotSupportedException) { check(await db.ShoppingLists.AnyAsync(x => x.Id == accessList), "Unsafe downgrade preserves shopping content and consumed identity history"); }
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private sealed class ShoppingClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    private sealed class FailedNotifications : IShoppingNotifications
    {
        public Task NotifyAsync(Guid listId, CancellationToken ct) => throw new IOException("Injected notification outage after commit");
    }
    private sealed class ShoppingWriteFailure : DbCommandInterceptor
    {
        public int Writes { get; private set; }
        private static bool ShoppingWrite(DbCommand command) => new[] { "INSERT", "UPDATE", "DELETE" }.Any(x => command.CommandText.TrimStart().StartsWith(x)) && command.CommandText.Contains("Shopping");
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (ShoppingWrite(command) && Writes > 0) throw new InvalidOperationException("Injected failure after a shopping write");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (ShoppingWrite(command)) Writes++;
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
    private sealed class ShoppingTransactionGuard : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT") && (command.CommandText.Contains("Shopping") || command.CommandText.Contains("AspNetUsers")))
            {
                if (command.Transaction is null) throw new Exception("Shopping authorization/content read happened outside its transaction");
                Interlocked.Increment(ref Reads);
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
