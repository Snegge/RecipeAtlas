using Recipoke.Api.Common.Application;
namespace Recipoke.Api.Common.Http;

public sealed class OperationFailures(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationFailure failure)
        {
            if (failure.Errors is not null)
                await Results.ValidationProblem(failure.Errors, statusCode: failure.StatusCode, title: failure.Message).ExecuteAsync(context);
            else await Results.Problem(statusCode: failure.StatusCode, title: failure.Message,
                extensions: new Dictionary<string, object?> { ["code"] = failure.Code }).ExecuteAsync(context);
        }
    }
}
