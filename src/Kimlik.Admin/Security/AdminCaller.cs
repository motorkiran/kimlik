using System.Diagnostics;
using System.Net;
using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;

namespace Kimlik.Admin.Security;

/// <summary>
/// The caller that Application handlers see during an admin panel operation: the signed-in administrator. A circuit
/// has no HTTP request to describe the caller, so each operation's scope gets the administrator put here.
/// </summary>
public sealed class AdminCaller : IRequestContext
{
    private AdminSession? _session;

    public bool IsSet => _session is not null;

    public AuditActor Actor => AuditActor.User(Session.UserId);

    public IReadOnlySet<string> Permissions => Session.Permissions;

    public IPAddress? IpAddress => Session.IpAddress;

    public string? UserAgent => Session.UserAgent;

    public string? CorrelationId => Activity.Current?.TraceId.ToString();

    private AdminSession Session => _session ?? throw new InvalidOperationException("No administrator is set for this operation.");

    internal void Set(AdminSession session) => _session = session;
}
