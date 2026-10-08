using System.Net;
using Kimlik.Domain.Auditing;

namespace Kimlik.Application.Abstractions;

/// <summary>Information about the caller of the current operation; empty outside of a request.</summary>
public interface IRequestContext
{
    AuditActor Actor { get; }

    /// <summary>The permissions of the caller, from its access token or session; empty for anonymous callers.</summary>
    IReadOnlySet<string> Permissions { get; }

    IPAddress? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}
