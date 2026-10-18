namespace Recipoke.Api.Common.Application;

// A narrow application failure translated centrally to the existing ProblemDetails shape.
public sealed class OperationFailure(int statusCode, string message, string code = "operation_failed",
    Dictionary<string, string[]>? errors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}
