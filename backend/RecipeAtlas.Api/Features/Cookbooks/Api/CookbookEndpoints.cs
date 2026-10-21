namespace Recipoke.Api.Features.Cookbooks.Api;

public static class CookbookEndpoints
{
    private static VersionPrecondition IfMatch(HttpRequest request) => new(request.Headers.IfMatch.ToString(), true);

    public static void Map(WebApplication app)
    {
        var books = app.MapGroup("/api/cookbooks").RequireAuthorization();
        books.MapGet("/", async (CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.ListAsync(ct))).Produces<IReadOnlyList<CookbookResponse>>();
        books.MapPost("/", async (CreateCookbookInput input, CookbookOperations operation, CancellationToken ct) =>
        {
            var book = await operation.CreateAsync(input, ct);
            return Results.Created($"/api/cookbooks/{book.Id}", book);
        }).Produces<CookbookResponse>(201);
        books.MapGet("/{id:guid}", async (Guid id, CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.GetAsync(id, ct))).Produces<CookbookResponse>();
        books.MapPut("/{id:guid}", async (Guid id, CookbookNameInput input, CookbookOperations operation, CancellationToken ct) =>
            Results.Ok(await operation.RenameAsync(id, input, ct))).Produces<CookbookResponse>();
        books.MapDelete("/{id:guid}", async (Guid id, HttpRequest request, CookbookOperations operation, CancellationToken ct) =>
        {
            await operation.DeleteAsync(id, IfMatch(request), ct);
            return Results.NoContent();
        }).Produces(204);
        books.MapGet("/{id:guid}/image", async (Guid id, CookbookMedia media, CancellationToken ct) =>
        {
            var image = await media.GetAsync(id, ct);
            return Results.File(image.Bytes, image.ContentType);
        }).Produces(200, contentType: "image/png", additionalContentTypes: ["image/jpeg", "image/webp"]);
        books.MapPut("/{id:guid}/image", async (Guid id, HttpRequest request, CookbookMedia media, CancellationToken ct) =>
            Results.Ok(await media.ReplaceAsync(id, request.Body, request.ContentLength, IfMatch(request), ct))).Produces<CookbookResponse>();
        books.MapDelete("/{id:guid}/image", async (Guid id, HttpRequest request, CookbookMedia media, CancellationToken ct) =>
            Results.Ok(await media.RemoveAsync(id, IfMatch(request), ct))).Produces<CookbookResponse>();
        books.MapDelete("/{id:guid}/invitations/{userId:guid}", async (Guid id, Guid userId, HttpRequest request, CookbookInvitations operation, CancellationToken ct) =>
            Results.Ok(await operation.CancelAsync(id, userId, IfMatch(request), ct))).Produces<VersionResponse>();
        var inbox = app.MapGroup("/api/cookbook-invitations").RequireAuthorization();
        inbox.MapGet("/", async (CookbookInvitations operation, CancellationToken ct) => Results.Ok(await operation.InboxAsync(ct))).Produces<IReadOnlyList<CookbookInvitationResponse>>();
        inbox.MapPost("/{id:guid}", async (Guid id, RespondToCookbookInvitationInput input, CookbookInvitations operation, CancellationToken ct) =>
        {
            await operation.RespondAsync(id, input.Accept, new(input?.ExpectedVersion), ct);
            return Results.NoContent();
        }).Produces(204);
        books.MapGet("/{id:guid}/members", async (Guid id, CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.MembersAsync(id, ct))).Produces<CookbookMembersResponse>();
        books.MapPost("/{id:guid}/members", async (Guid id, AddMemberInput input, CookbookOperations operation, CancellationToken ct) =>
            Results.Ok(await operation.AddMemberAsync(id, input, ct))).Produces<VersionResponse>();
        books.MapPut("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, MemberRoleInput input, CookbookOperations operation, CancellationToken ct) =>
            Results.Ok(await operation.ChangeMemberAsync(id, userId, input.Role, false, new(input?.ExpectedVersion), ct))).Produces<VersionResponse>();
        books.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, HttpRequest request, CookbookOperations operation, CancellationToken ct) =>
        {
            var result = await operation.ChangeMemberAsync(id, userId, null, true, IfMatch(request), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        }).Produces<VersionResponse>().Produces(204);
        books.MapPost("/{id:guid}/leave", async (Guid id, ExpectedVersionInput? input, CookbookOperations operation, CancellationToken ct) =>
        {
            await operation.LeaveAsync(id, new(input?.ExpectedVersion), ct);
            return Results.NoContent();
        }).Produces(204);
    }
}
