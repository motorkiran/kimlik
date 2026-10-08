using System.Text.Json;
using System.Text.Json.Serialization;
using Kimlik.Contracts;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;

namespace Kimlik.Server.Api;

/// <summary>The Management API: REST over JSON under <see cref="BasePath"/>, called with access tokens for Kimlik's API.</summary>
internal static class ManagementApi
{
    public const string BasePath = "/api/v1";

    public const string DocumentName = "v1";

    public static IServiceCollection AddManagementApi(this IServiceCollection services)
    {
        services.AddOpenIddict().AddValidation(options =>
        {
            // The issuer and keys come from the server in this process; see ConfigureTokenKeyCredentials.
            options.UseLocalServer();
            options.AddAudiences(KimlikScopes.Api);

            // Revoked tokens and authorizations (sign-out, suspension, deletion) stop working at once,
            // instead of when the access token expires. Registered after UseLocalServer, which resets the former.
            options.EnableTokenEntryValidation();
            options.EnableAuthorizationEntryValidation();

            options.UseAspNetCore();
        });

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Enums travel as their camelCase names; numbers would tie clients to declaration order.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
        services.Configure<ProblemDetailsOptions>(options => options.CustomizeProblemDetails = context =>
        {
            // Malformed requests get a code too, like every other API error.
            if (context.ProblemDetails.Status == StatusCodes.Status400BadRequest)
            {
                context.ProblemDetails.Extensions.TryAdd(ApiResults.CodeExtension, "request.invalid");
            }
        });
        services.AddValidation();

        services.AddOpenApi(DocumentName, options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith(BasePath[1..], StringComparison.Ordinal) == true;
            options.AddDocumentTransformer<ManagementApiDocumentTransformer>();
            options.AddOperationTransformer<PermissionOperationTransformer>();
        });

        return services;
    }

    public static IEndpointRouteBuilder MapManagementApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(BasePath)
            .MapUserEndpoints()
            .MapPermissionEndpoints()
            .MapRoleEndpoints()
            .MapApiResourceEndpoints();

        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options => options.WithTitle("Kimlik API"));

        return endpoints;
    }
}
