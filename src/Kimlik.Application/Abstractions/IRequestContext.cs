using System.Net;
using Kimlik.Domain.Auditing;

namespace Kimlik.Application.Abstractions;

/// <summary>Information about the caller of the current operation; empty outside of a request.</summary>
public interface IRequestContext
{
    AuditActor Actor { get; }

    IPAddress? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}
