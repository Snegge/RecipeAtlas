namespace Recipoke.Api.Common.Application;

// HTTP-independent representation of either a JSON precondition or a single strong If-Match tag.
public readonly record struct VersionPrecondition(string? Value, bool IsEntityTag = false)
{
    public Guid Require()
    {
        if (Value is null || (IsEntityTag && string.IsNullOrWhiteSpace(Value)))
            throw new OperationFailure(428, "Reload this page before saving; a version is required.", "concurrency_precondition_required");
        var token = Value;
        if (IsEntityTag)
        {
            token = token.Trim();
            if (token.Length < 2 || token[0] != '"' || token[^1] != '"') throw Malformed();
            token = token[1..^1];
        }
        if (token.Length != 36 || !Guid.TryParseExact(token, "D", out var version) || version == Guid.Empty) throw Malformed();
        return version;
    }

    public OperationFailure Conflict() => new(IsEntityTag ? 412 : 409,
        "This item changed after you loaded it. Review the latest version before saving.", "concurrency_conflict");

    private static OperationFailure Malformed() => new(400,
        "Supply one valid version; If-Match must contain a single quoted strong version, without wildcards.", "invalid_concurrency_precondition");
}
