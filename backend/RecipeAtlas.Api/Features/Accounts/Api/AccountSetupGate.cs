namespace Recipoke.Api.Features.Accounts.Api;
// The application operation checks setup; transport middleware never exposes unassigned legacy data.
public sealed class AccountSetupGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, Recipoke.Api.Features.Accounts.Application.AdministratorSetup setup)
    {
        if (context.Request.Path.StartsWithSegments("/api") && !await setup.IsCompleteAsync(context.RequestAborted))
        { await Results.Problem(statusCode: 503, title: "Administrator setup is required on the server.").ExecuteAsync(context); return; }
        await next(context);
    }
}
