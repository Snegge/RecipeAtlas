using System.Text.Json.Serialization;
namespace Recipoke.Api.Features.Authentication.Api;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginInput(string? Username, string? Password);
