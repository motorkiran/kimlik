using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.Admin.Security;

/// <summary>The administrator signed in to this circuit: who they are and what they may do in Kimlik.</summary>
public sealed class AdminSession
{
    private Guid? _userId;

    public bool IsLoaded => _userId is not null;

    public Guid UserId => _userId ?? throw new InvalidOperationException("The admin session is not loaded yet.");

    public string? Email { get; private set; }

    /// <summary>The administrator's permissions for the whole installation, from their global roles.</summary>
    public IReadOnlySet<string> Permissions { get; private set; } = new HashSet<string>();

    /// <summary>Where the administrator connects from, for the audit trail.</summary>
    public IPAddress? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public bool Has(string permission) => Permissions.Contains(permission);

    internal void Load(Guid userId, string? email, IReadOnlySet<string> permissions, IPAddress? ipAddress, string? userAgent)
    {
        _userId = userId;
        Email = email;
        Permissions = permissions;
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    /// <summary>Loads the session for a plain request, without the circuit that loads it for the panel's pages.</summary>
    internal async Task LoadAsync(HttpContext context, IServiceScopeFactory scopeFactory) => Load(
        Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
        context.User.FindFirstValue(ClaimTypes.Email),
        await AdminAccess.PermissionsOfAsync(scopeFactory, context.User),
        context.Connection.RemoteIpAddress,
        context.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null);

    internal void Refresh(IReadOnlySet<string> permissions) => Permissions = permissions;
}
