using System.ComponentModel;
using Kimlik.Application.ApiKeys;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>Every API key of the installation, and their verification for resource servers.</summary>
internal static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder api)
    {
        var keys = api.MapGroup("api-keys").WithTags("API keys");

        keys.MapGet(string.Empty, ListAsync).WithName("ListApiKeys").WithSummary("List API keys")
            .WithDescription("Lists API keys in creation order, revoked and expired ones included. Owners create keys through the Account API.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.ApiKeysRead);

        keys.MapDelete("{id:guid}", RevokeAsync).WithName("RevokeApiKey").WithSummary("Revoke an API key")
            .WithDescription("The key stops working at once, and stays listed with the time it was revoked.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ApiKeysWrite);

        keys.MapPost("verify", VerifyAsync).WithName("VerifyApiKey").WithSummary("Verify an API key")
            .WithDescription("For resource servers that accept API keys: whether the key works, whose it is and what it may do now. "
                + "An unknown, expired or revoked key is reported as not active. Resource servers may cache the answer briefly.")
            .ProducesValidationProblem()
            .RequirePermission(SystemPermissions.ApiKeysVerify);

        return api;
    }

    private static async Task<Results<Ok<Page<ApiKeyResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Only the keys of this user.")] Guid? userId,
        [Description("Only the keys of this organization.")] Guid? organizationId,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListApiKeysHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListApiKeysQuery(userId, organizationId, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(Guid id, RevokeApiKeyHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Ok<ApiKeyVerificationResponse>> VerifyAsync(
        VerifyApiKeyRequest request, VerifyApiKeyHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(request, cancellationToken));
}
