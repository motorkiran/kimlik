using System.Net;

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

    internal void Refresh(IReadOnlySet<string> permissions) => Permissions = permissions;
}
