using Microsoft.EntityFrameworkCore;
using Recipoke.Domain.Shopping;

namespace Recipoke.Api.Infrastructure.Persistence.Configurations;

public static class ShoppingConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var lists = model.Entity<ShoppingList>();
        lists.Property(x => x.Name).HasMaxLength(100);
        lists.Property(x => x.Version).IsConcurrencyToken();
        lists.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        var members = model.Entity<ShoppingMembership>();
        members.HasKey(x => new { x.ListId, x.UserId });
        members.HasOne<ShoppingList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        members.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var invitations = model.Entity<ShoppingInvitation>();
        invitations.HasIndex(x => new { x.ListId, x.UserId }).IsUnique();
        invitations.HasOne<ShoppingList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        invitations.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var items = model.Entity<ShoppingItem>();
        items.Property(x => x.Text).HasMaxLength(500);
        items.Property(x => x.Version).IsConcurrencyToken();
        items.Property(x => x.UndoUntilUtc).HasConversion<long?>();
        items.HasIndex(x => new { x.ListId, x.Position }).IsUnique();
        items.HasOne<ShoppingList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        var reservations = model.Entity<ShoppingReservation>();
        reservations.HasKey(x => x.ItemId);
        reservations.Property(x => x.ExpiresAtUtc).HasConversion<long>();
        reservations.HasOne<ShoppingItem>().WithOne().HasForeignKey<ShoppingReservation>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        reservations.HasOne<ShoppingList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        reservations.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        var tokens = model.Entity<ShoppingReservationToken>();
        tokens.HasKey(x => new { x.ItemId, x.Token });
        tokens.HasOne<ShoppingItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        var receipts = model.Entity<ShoppingReceipt>();
        receipts.HasKey(x => new { x.ListId, x.ActorUserId, x.OperationId });
        receipts.Property(x => x.Operation).HasMaxLength(40);
        receipts.Property(x => x.PayloadHash).HasMaxLength(64);
        receipts.HasOne<ShoppingList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ShoppingListTombstone>().HasKey(x => x.Id);
    }
}
