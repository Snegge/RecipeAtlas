using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace Recipoke.Api.Common.Http;

public static class ApiDescription
{
    public static IServiceCollection AddApiDescription(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, ct) =>
            {
                document.Info.Title = "Recipoke API";
                document.Info.Description = "Invitation-only accounts and cookbook-scoped recipes. Quantities are strings. Development-only description.";
                document.Components ??= new();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["sessionCookie"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = "Recipoke.Session"
                };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, ct) =>
            {
                var path = context.Description.RelativePath ?? "";
                var method = context.Description.HttpMethod ?? "GET";
                if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
                {
                    operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("sessionCookie", context.Document)] = [] }];
                    operation.Responses ??= new();
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Active account authentication required" });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Insufficient account or cookbook permissions" });
                }
                if (path.StartsWith("api/") && method is not ("GET" or "HEAD" or "OPTIONS"))
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "X-RecipeAtlas",
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Existing browser write protection; value must be 1.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    });
                    operation.Responses ??= new();
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Missing write header" });
                }
                if (path.StartsWith("api/"))
                {
                    operation.Responses ??= new();
                    operation.Responses.TryAdd("503", new OpenApiResponse { Description = "Administrator setup or required provider configuration is missing" });
                    if (path.Contains("cookbooks/{"))
                        operation.Responses.TryAdd("404", new OpenApiResponse { Description = "Resource missing or outside accessible cookbooks" });
                }
                var versioned = (path.StartsWith("api/cookbooks/{") &&
                    (method is "PUT" or "DELETE" || (method == "POST" && (path.EndsWith("/members") || path.EndsWith("/leave"))))) ||
                    (path.StartsWith("api/cookbook-invitations/{") && method == "POST");
                if (versioned)
                {
                    var header = method == "DELETE" || (method == "PUT" && path.EndsWith("/image"));
                    operation.Responses ??= new();
                    operation.Responses.TryAdd("428", new OpenApiResponse { Description = "concurrency_precondition_required: reload and submit a version" });
                    operation.Responses.TryAdd("400", new OpenApiResponse { Description = "Malformed version or invalid command" });
                    operation.Responses.TryAdd(header ? "412" : "409", new OpenApiResponse { Description = "concurrency_conflict: review current authorized state; no automatic overwrite" });
                    if (header)
                    {
                        operation.Parameters ??= [];
                        operation.Parameters.Add(new OpenApiParameter
                        {
                            Name = "If-Match", In = ParameterLocation.Header, Required = true,
                            Description = "One quoted strong aggregate version (UUID). Weak tags, lists and wildcard overwrite are rejected. Required also for raw binary photo PUT.",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                        });
                    }
                }
                // Description only: Accepts metadata would change raw-image request validation.
                if (method == "PUT" && (path.EndsWith("/image") || path.EndsWith("/avatar")))
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/octet-stream"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } }
                        }
                    };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
