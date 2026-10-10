using System.Security.Cryptography.X509Certificates;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Access;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using SsoProtocol = Kimlik.Domain.Organizations.SsoProtocol;

namespace Kimlik.Application.Organizations;

/// <summary>Lists connections in creation order, all of them or one organization's.</summary>
public sealed record ListSsoConnectionsQuery(Guid? OrganizationId, string? Cursor, int? Limit);

public sealed class ListSsoConnectionsHandler(IKimlikDbContext context)
{
    public async Task<Result<Page<SsoConnectionResponse>>> HandleAsync(ListSsoConnectionsQuery query, CancellationToken cancellationToken)
    {
        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CommonErrors.InvalidCursor;
        }

        var connections = context.SsoConnections.AsNoTracking();
        if (query.OrganizationId is { } organizationId)
        {
            connections = connections.Where(connection => connection.OrganizationId == organizationId);
        }

        if (after is { } afterId)
        {
            connections = connections.Where(connection => connection.Id > afterId);
        }

        var (page, nextCursor) = await Cursor.ReadPageAsync(connections.OrderBy(connection => connection.Id), query.Limit, connection => connection.Id, cancellationToken);
        return new Page<SsoConnectionResponse>([.. page.Select(connection => connection.ToResponse())], nextCursor);
    }
}

public sealed class GetSsoConnectionHandler(IKimlikDbContext context)
{
    public async Task<Result<SsoConnectionResponse>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await context.SsoConnections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken) is { } connection
            ? connection.ToResponse()
            : SsoErrors.NotFound;
}

/// <summary>
/// Sets up an organization's identity provider. It can sign in anyone with an address in its domains, administrators
/// included, so only callers holding every installation-wide system permission manage connections.
/// </summary>
public sealed class CreateSsoConnectionHandler(
    IKimlikDbContext context, AccessGuard guard, ISecretEncryption encryption, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SsoConnectionResponse>> HandleAsync(CreateSsoConnectionRequest request, CancellationToken cancellationToken)
    {
        var allowed = guard.EnsureHoldsAllSystemPermissions();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var protocol = request.Protocol == Contracts.Management.SsoProtocol.Saml ? SsoProtocol.Saml : SsoProtocol.OpenIdConnect;
        if (protocol == SsoProtocol.OpenIdConnect && !SsoClientSecrets.IsValid(request.ClientSecret))
        {
            return SsoErrors.InvalidClientSecret;
        }

        if (!await context.Organizations.AnyAsync(organization => organization.Id == request.OrganizationId, cancellationToken))
        {
            return OrganizationErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var settings = new SsoConnectionSettings(request.Name, request.Issuer, request.Domains ?? [], request.Enabled)
        {
            ClientId = request.ClientId,
            SignOnUrl = request.SignOnUrl,
            Certificate = request.Certificate,
        };
        var created = SsoConnection.Create(request.OrganizationId, protocol, settings, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var connection = created.Value;
        if (protocol == SsoProtocol.OpenIdConnect)
        {
            connection.SetClientSecret(SsoClientSecrets.Encrypt(encryption, connection.Id, request.ClientSecret!), now);
        }

        context.SsoConnections.Add(connection);
        auditLog.Record(AuditActions.SsoConnectionCreated, AuditSubject.SsoConnection(connection.Id), connection.AuditData(), organizationId: connection.OrganizationId);
        return await SsoConnectionStore.SaveAsync(context, connection, cancellationToken);
    }
}

public sealed class UpdateSsoConnectionHandler(
    IKimlikDbContext context, AccessGuard guard, ISecretEncryption encryption, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<SsoConnectionResponse>> HandleAsync(Guid id, UpdateSsoConnectionRequest request, CancellationToken cancellationToken)
    {
        var allowed = guard.EnsureHoldsAllSystemPermissions();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (await context.SsoConnections.SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return SsoErrors.NotFound;
        }

        if (request.ClientSecret is not null && !SsoClientSecrets.IsValid(request.ClientSecret))
        {
            return SsoErrors.InvalidClientSecret;
        }

        var now = timeProvider.GetUtcNow();
        var settings = new SsoConnectionSettings(
            request.Name ?? connection.Name,
            request.Issuer ?? connection.Issuer,
            request.Domains ?? [.. connection.Domains.Select(domain => domain.Domain)],
            request.Enabled ?? connection.Enabled)
        {
            ClientId = request.ClientId ?? connection.ClientId,
            SignOnUrl = request.SignOnUrl ?? connection.SignOnUrl,
            Certificate = request.Certificate ?? connection.Certificate,
        };
        var updated = connection.Update(settings, now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var secretChanged = request.ClientSecret is not null && connection.Protocol == SsoProtocol.OpenIdConnect;
        if (secretChanged)
        {
            connection.SetClientSecret(SsoClientSecrets.Encrypt(encryption, connection.Id, request.ClientSecret!), now);
        }

        var data = connection.AuditData();
        data["client_secret_changed"] = secretChanged;
        auditLog.Record(AuditActions.SsoConnectionUpdated, AuditSubject.SsoConnection(id), data, organizationId: connection.OrganizationId);
        return await SsoConnectionStore.SaveAsync(context, connection, cancellationToken);
    }
}

/// <summary>Deletes a connection. Its users keep their accounts and memberships, and sign in the other ways again.</summary>
public sealed class DeleteSsoConnectionHandler(IKimlikDbContext context, AccessGuard guard, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var allowed = guard.EnsureHoldsAllSystemPermissions();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (await context.SsoConnections.SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken) is not { } connection)
        {
            return SsoErrors.NotFound;
        }

        context.SsoConnections.Remove(connection);
        auditLog.Record(AuditActions.SsoConnectionDeleted, AuditSubject.SsoConnection(id), connection.AuditData(), organizationId: connection.OrganizationId);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>The client secrets Kimlik holds at organizations' providers, encrypted because Kimlik sends them.</summary>
public static class SsoClientSecrets
{
    /// <summary>The purpose the secrets are encrypted for.</summary>
    public const string Purpose = "sso.client-secret";

    public static bool IsValid(string? secret) => secret is { Length: > 0 and <= SsoConnection.ClientSecretMaxLength } && !string.IsNullOrWhiteSpace(secret);

    public static string Encrypt(ISecretEncryption encryption, Guid connectionId, string secret) => encryption.Encrypt(secret, Purpose, connectionId);

    /// <summary>The client secret of an OpenID Connect connection; SAML ones have none.</summary>
    public static string? Decrypt(ISecretEncryption encryption, SsoConnection connection) =>
        connection.EncryptedClientSecret is { } secret ? encryption.Decrypt(secret, Purpose, connection.Id) : null;
}

internal static class SsoConnectionStore
{
    /// <summary>Saves the connection; a domain that another connection took meanwhile fails on the unique key.</summary>
    public static async Task<Result<SsoConnectionResponse>> SaveAsync(IKimlikDbContext context, SsoConnection connection, CancellationToken cancellationToken)
    {
        var domains = connection.Domains.Select(domain => domain.Domain).ToList();
        if (await context.SsoDomains.AnyAsync(domain => domains.Contains(domain.Domain) && domain.ConnectionId != connection.Id, cancellationToken))
        {
            return SsoErrors.DomainTaken;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return SsoErrors.DomainTaken;
        }

        return connection.ToResponse();
    }

    public static Dictionary<string, object?> AuditData(this SsoConnection connection) => new()
    {
        ["name"] = connection.Name,
        ["protocol"] = connection.Protocol.ToString(),
        ["issuer"] = connection.Issuer,
        ["domains"] = connection.Domains.Select(domain => domain.Domain).Order(StringComparer.Ordinal).ToArray(),
        ["enabled"] = connection.Enabled,
    };

    public static SsoConnectionResponse ToResponse(this SsoConnection connection) => new(
        connection.Id,
        connection.OrganizationId,
        connection.Name,
        connection.Protocol == SsoProtocol.Saml ? Contracts.Management.SsoProtocol.Saml : Contracts.Management.SsoProtocol.OpenIdConnect,
        connection.Issuer,
        connection.ClientId,
        connection.SignOnUrl,
        connection.Certificate,
        connection.Certificate is { } certificate ? ExpiryOf(certificate) : null,
        [.. connection.Domains.Select(domain => domain.Domain).Order(StringComparer.Ordinal)],
        connection.Enabled,
        connection.CreatedAt,
        connection.UpdatedAt);

    private static DateTimeOffset ExpiryOf(string certificate)
    {
        using var parsed = X509Certificate2.CreateFromPem(certificate);
        return new DateTimeOffset(parsed.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }
}
