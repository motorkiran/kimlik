using System.ComponentModel;
using Kimlik.Application.Organizations;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>Organizations' own identity providers, which sign in the people whose addresses are in their domains.</summary>
internal static class SsoConnectionEndpoints
{
    private const string Escalation =
        "A connection can sign in anyone with an address in its domains, administrators included, so changing connections also takes every installation-wide system permission.";

    public static IEndpointRouteBuilder MapSsoConnectionEndpoints(this IEndpointRouteBuilder api)
    {
        var connections = api.MapGroup("sso-connections").WithTags("SSO connections");

        connections.MapGet(string.Empty, ListAsync).WithName("ListSsoConnections").WithSummary("List SSO connections")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        connections.MapPost(string.Empty, CreateAsync).WithName("CreateSsoConnection").WithSummary("Set up an organization's identity provider")
            .WithDescription($"Kimlik discovers the provider's endpoints from its issuer and signs in over OpenID Connect; register `{{PublicUrl}}/signin/sso/callback` as the redirect URI there. While the connection is enabled, people with an address in its domains sign in only through it. {Escalation}")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        connections.MapGet("{id:guid}", GetAsync).WithName("GetSsoConnection").WithSummary("Get an SSO connection")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsRead);

        connections.MapPatch("{id:guid}", UpdateAsync).WithName("UpdateSsoConnection").WithSummary("Change an SSO connection")
            .WithDescription($"Changes the name, provider, client, domains or whether it is enabled; disabling it lets its people sign in the other ways again. {Escalation}")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        connections.MapDelete("{id:guid}", DeleteAsync).WithName("DeleteSsoConnection").WithSummary("Delete an SSO connection")
            .WithDescription($"Its people keep their accounts and memberships, and sign in the other ways again. {Escalation}")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.OrganizationsWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<SsoConnectionResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Only the connections of this organization.")] Guid? organizationId,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListSsoConnectionsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListSsoConnectionsQuery(organizationId, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<SsoConnectionResponse>, ProblemHttpResult>> CreateAsync(
        CreateSsoConnectionRequest request, CreateSsoConnectionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(connection => $"/api/v1/sso-connections/{connection.Id}");

    private static async Task<Results<Ok<SsoConnectionResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetSsoConnectionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<SsoConnectionResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateSsoConnectionRequest request, UpdateSsoConnectionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteSsoConnectionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
