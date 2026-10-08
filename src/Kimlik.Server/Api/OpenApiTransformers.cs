using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kimlik.Server.Api;

internal sealed class ManagementApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string SecuritySchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Kimlik API",
            Version = ManagementApi.DocumentName,
            Description = "The Management API manages the users, organizations, access and clients of a Kimlik installation; "
                + "calls take an access token for the `kimlik` scope, issued to a service client or an administrator, "
                + "carrying the permission each operation names. The Account API under `/me` acts for the signed-in user. "
                + "Errors are RFC 9457 problem details with a stable, machine-readable `code`.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "An access token from the token endpoint, for the `kimlik` scope.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>Documents who may call each operation, and the responses for callers who may not.</summary>
internal sealed class PermissionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        string requirement, forbidden;

        if (metadata.OfType<RequiredPermission>().LastOrDefault() is { } required)
        {
            var permissions = string.Join(" and ", required.Permissions.Select(permission => $"`{permission}`"));
            requirement = $"Requires the {permissions} permission{(required.Permissions.Count > 1 ? "s" : string.Empty)}.";
            forbidden = $"The access token does not carry {permissions}.";
        }
        else if (metadata.OfType<RequiredSignedInUser>().Any())
        {
            requirement = "Acts for the signed-in user: requires an access token for the `kimlik` scope issued to a user.";
            forbidden = "The access token was issued to a service client, or the user's roles do not allow the operation.";
        }
        else
        {
            return Task.CompletedTask;
        }

        operation.Description = operation.Description is null ? requirement : $"{operation.Description}\n\n{requirement}";
        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ManagementApiDocumentTransformer.SecuritySchemeName, context.Document)] = [] }];

        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "The access token is missing, invalid or revoked." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = forbidden });

        return Task.CompletedTask;
    }
}
