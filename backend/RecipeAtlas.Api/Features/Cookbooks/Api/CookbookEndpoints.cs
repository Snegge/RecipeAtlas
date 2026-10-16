namespace Recipoke.Api.Features.Cookbooks.Api;
public static class CookbookEndpoints
{
    public static void Map(WebApplication app)
    {
        var books = app.MapGroup("/api/cookbooks").RequireAuthorization();
        books.MapGet("/", async (CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.ListAsync(ct))).Produces<IReadOnlyList<CookbookResponse>>();
        books.MapPost("/", async (CookbookNameInput input, CookbookOperations operation, CancellationToken ct) =>
        { var book = await operation.CreateAsync(input, ct); return Results.Created($"/api/cookbooks/{book.Id}", book); }).Produces<CookbookResponse>(201);
        books.MapGet("/{id:guid}", async (Guid id, CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.GetAsync(id, ct))).Produces<CookbookResponse>();
        books.MapPut("/{id:guid}", async (Guid id, CookbookNameInput input, CookbookOperations operation, CancellationToken ct) =>
        { await operation.RenameAsync(id, input, ct); return Results.NoContent(); }).Produces(204);
        books.MapGet("/{id:guid}/members", async (Guid id, CookbookOperations operation, CancellationToken ct) => Results.Ok(await operation.MembersAsync(id, ct))).Produces<IReadOnlyList<MemberResponse>>();
        books.MapPost("/{id:guid}/members", async (Guid id, AddMemberInput input, CookbookOperations operation, CancellationToken ct) =>
        { await operation.AddMemberAsync(id, input, ct); return Results.NoContent(); }).Produces(204);
        books.MapPut("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, MemberRoleInput input, CookbookOperations operation, CancellationToken ct) =>
        { await operation.ChangeMemberAsync(id, userId, input.Role, false, ct); return Results.NoContent(); }).Produces(204);
        books.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, CookbookOperations operation, CancellationToken ct) =>
        { await operation.ChangeMemberAsync(id, userId, null, true, ct); return Results.NoContent(); }).Produces(204);
        books.MapPost("/{id:guid}/leave", async (Guid id, CookbookOperations operation, CancellationToken ct) =>
        { await operation.LeaveAsync(id, ct); return Results.NoContent(); }).Produces(204);
    }
}
