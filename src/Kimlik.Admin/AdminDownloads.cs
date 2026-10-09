using System.Security.Claims;
using System.Text.Json;
using Kimlik.Admin.Security;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Access;
using Kimlik.Domain.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kimlik.Admin;

/// <summary>Files the admin panel offers for download, behind the same policy as its pages.</summary>
internal static class AdminDownloads
{
    public static void MapAdminDownloads(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/admin/users/{id:guid}/export", ExportUserAsync).RequireAuthorization(AdminAccess.Policy);

    /// <summary>A user's personal data, as a JSON file, exported by the signed-in administrator, who needs <c>kimlik.users:read</c>.</summary>
    private static async Task<IResult> ExportUserAsync(
        Guid id,
        HttpContext context,
        AdminSession session,
        AdminOperations operations,
        IServiceScopeFactory scopeFactory,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        // A plain request, without the circuit that loads the session for the panel's pages.
        session.Load(
            Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            context.User.FindFirstValue(ClaimTypes.Email),
            await AdminAccess.PermissionsOfAsync(scopeFactory, context.User),
            context.Connection.RemoteIpAddress,
            context.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null);
        if (!session.Has(SystemPermissions.UsersRead))
        {
            return Results.Forbid();
        }

        var export = await operations.RunAsync<ExportUserDataHandler, Result<PersonalDataExport>>(
            SystemPermissions.UsersRead, handler => handler.HandleAsync(id, cancellationToken));
        return export.IsFailure
            ? Results.NotFound()
            : Results.File(JsonSerializer.SerializeToUtf8Bytes(export.Value, json.Value.SerializerOptions), "application/json", $"kimlik-user-{id}.json");
    }
}
