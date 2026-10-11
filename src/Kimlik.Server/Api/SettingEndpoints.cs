using Kimlik.Application.Settings;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>The product settings that can change at runtime (ADR 0002).</summary>
internal static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("settings").WithTags("Settings");

        settings.MapGet(string.Empty, (GetSettingsHandler handler) => TypedResults.Ok(handler.Handle()))
            .WithName("ListSettings").WithSummary("List the settings that can change at runtime")
            .WithDescription("Each comes with its value in effect, except for secrets, and where that comes from. Settings set in the environment cannot change at runtime.")
            .RequirePermission(SystemPermissions.SettingsRead);

        settings.MapPatch(string.Empty, UpdateAsync).WithName("UpdateSettings").WithSummary("Change settings")
            .WithDescription("Sets the given settings, by configuration path, and returns them all; `null` returns a setting to its configured or default value. The values are checked as a whole, as at startup, and apply at once, and on other instances within 15 seconds. Changing settings also takes every installation-wide system permission.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.SettingsWrite);

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<SettingResponse>>, ProblemHttpResult>> UpdateAsync(
        UpdateSettingsRequest request, UpdateSettingsHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToOk();
}
