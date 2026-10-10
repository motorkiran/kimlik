using System.ComponentModel;
using Kimlik.Application.Clients;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kimlik.Server.Api;

internal static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder api)
    {
        var clients = api.MapGroup("clients").WithTags("Clients");

        clients.MapGet(string.Empty, ListAsync)
            .WithName("ListClients")
            .WithSummary("List clients")
            .WithDescription("Lists clients in creation order.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.ClientsRead);

        clients.MapPost(string.Empty, CreateAsync)
            .WithName("CreateClient")
            .WithSummary("Register a client")
            .WithDescription("The response carries the secret of a web or service client; it is not shown again.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.ClientsWrite);

        clients.MapGet("{id:guid}", GetAsync)
            .WithName("GetClient")
            .WithSummary("Get a client")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsRead);

        clients.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdateClient")
            .WithSummary("Update a client")
            .WithDescription("JSON Merge Patch: omitted properties keep their value, and lists are replaced as a whole.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        clients.MapPost("{id:guid}/secret", RegenerateSecretAsync)
            .WithName("RegenerateClientSecret")
            .WithSummary("Regenerate a client's secret")
            .WithDescription("The previous secret stops working at once, or after `keepPreviousSecretForDays` (up to 30), so that the client "
                + "switches without downtime; the body is optional. Tokens already issued stay valid until they expire.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        clients.MapPut("{id:guid}/roles", SetRolesAsync)
            .WithName("SetClientRoles")
            .WithSummary("Replace a service client's roles")
            .WithDescription("Granting system permissions requires holding them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        clients.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeleteClient")
            .WithSummary("Delete a client")
            .WithDescription("Also revokes everything issued to it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.ClientsWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<ClientResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the client ID or display name.")] string? q,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListClientsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListClientsQuery(q, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<CreatedClientResponse>, ProblemHttpResult>> CreateAsync(
        CreateClientRequest request, CreateClientHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(created => $"{ManagementApi.BasePath}/clients/{created.Client.Id}");

    private static async Task<Results<Ok<ClientResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetClientHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<ClientResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateClientRequest request, UpdateClientHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<Ok<ClientSecretResponse>, ProblemHttpResult>> RegenerateSecretAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegenerateClientSecretRequest? request,
        RegenerateClientSecretHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request ?? new RegenerateClientSecretRequest(), cancellationToken)).ToOk();

    private static async Task<Results<Ok<ClientResponse>, ProblemHttpResult>> SetRolesAsync(
        Guid id, SetRolesRequest request, SetClientRolesHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteClientHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
