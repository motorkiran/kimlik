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
            Title = "Kimlik Management API",
            Version = ManagementApi.DocumentName,
            Description = "Manage the users, access and clients of a Kimlik installation. Calls take an access token "
                + "for the `kimlik` scope, issued to a service client or an administrator, carrying the permission "
                + "each operation names. Errors are RFC 9457 problem details with a stable, machine-readable `code`.",
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

/// <summary>Documents the permission each operation requires, and the responses for a missing one.</summary>
internal sealed class PermissionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiredPermission>().LastOrDefault() is not { } required)
        {
            return Task.CompletedTask;
        }

        var requirement = $"Requires the `{required.Permission}` permission.";
        operation.Description = operation.Description is null ? requirement : $"{operation.Description}\n\n{requirement}";

        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ManagementApiDocumentTransformer.SecuritySchemeName, context.Document)] = [] }];

        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "The access token is missing, invalid or revoked." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = $"The access token does not carry `{required.Permission}`." });

        return Task.CompletedTask;
    }
}
