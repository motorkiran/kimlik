using Kimlik.Application.Provisioning;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class ProvisioningEndpoints
{
    public static IEndpointRouteBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder api)
    {
        var provisioning = api.MapGroup("provisioning").WithTags("Provisioning");

        provisioning.MapGet(string.Empty, ExportAsync)
            .WithName("ExportProvisioning")
            .WithSummary("Export the access model")
            .WithDescription("Describes permissions, roles, features, plans, API resources and clients as a provisioning document, without client secrets.")
            .RequirePermission(SystemPermissions.RolesRead, SystemPermissions.ClientsRead, SystemPermissions.PlansRead);

        // The document covers roles, clients and plans, so applying one takes the right to change all three.
        provisioning.MapPost(string.Empty, ApplyAsync)
            .WithName("ApplyProvisioning")
            .WithSummary("Apply a provisioning document")
            .WithDescription("Creates what is missing and updates what differs, in one transaction; nothing is deleted.")
            .ProducesValidationProblem()
            .RequirePermission(SystemPermissions.RolesWrite, SystemPermissions.ClientsWrite, SystemPermissions.PlansWrite);

        return api;
    }

    private static async Task<Ok<ProvisioningDocument>> ExportAsync(ExportProvisioningHandler handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(cancellationToken));

    private static async Task<Results<Ok<ProvisioningResult>, ProblemHttpResult>> ApplyAsync(
        ProvisioningDocument document, ApplyProvisioningHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(document, cancellationToken)).ToOk();
}
