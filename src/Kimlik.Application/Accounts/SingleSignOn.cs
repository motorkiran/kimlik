using Kimlik.Application.Abstractions;
using Kimlik.Application.Organizations;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Accounts;

/// <summary>An SSO connection as sign-in uses it, with the client secret of an OpenID Connect one.</summary>
public sealed class SsoProvider
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required SsoProtocol Protocol { get; init; }

    /// <summary>The issuer URL of an OpenID Connect provider, or the entity ID of a SAML one.</summary>
    public required string Issuer { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? SignOnUrl { get; init; }

    /// <summary>The certificate a SAML provider signs with, in PEM.</summary>
    public string? Certificate { get; init; }

    public required IReadOnlyList<string> Domains { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>When the connection last changed, which tells whether what was built from it is still current.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The provider of the linked logins and sessions of the connection's sign-ins.</summary>
    public string LoginProvider => SsoConnection.LoginProviderOf(Id);

    public bool Covers(string? email) => SsoConnection.DomainOf(email) is { } domain && Domains.Contains(domain, StringComparer.Ordinal);

    /// <inheritdoc cref="SsoConnection.IsOrganizationAccount"/>
    public bool IsOrganizationAccount(string? hostedDomain) => SsoConnection.IsOrganizationAccount(Issuer, Domains, hostedDomain);
}

/// <summary>Finds the SSO connections that sign people in.</summary>
public sealed class SsoDirectory(IKimlikDbContext context, ISecretEncryption encryption)
{
    public Task<bool> AnyEnabledAsync(CancellationToken cancellationToken) =>
        context.SsoConnections.AnyAsync(connection => connection.Enabled, cancellationToken);

    public async Task<SsoProvider?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.SsoConnections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken) is { } connection
            ? ToProvider(connection)
            : null;

    /// <summary>The enabled connection that covers the address's domain, which people with that address sign in through.</summary>
    public async Task<SsoProvider?> ForAddressAsync(string? email, CancellationToken cancellationToken)
    {
        if (SsoConnection.DomainOf(email) is not { } domain)
        {
            return null;
        }

        return await context.SsoConnections.AsNoTracking()
            .SingleOrDefaultAsync(connection => connection.Enabled && connection.Domains.Any(candidate => candidate.Domain == domain), cancellationToken) is { } connection
            ? ToProvider(connection)
            : null;
    }

    private SsoProvider ToProvider(SsoConnection connection) => new()
    {
        Id = connection.Id,
        OrganizationId = connection.OrganizationId,
        Name = connection.Name,
        Protocol = connection.Protocol,
        Issuer = connection.Issuer,
        ClientId = connection.ClientId,
        ClientSecret = SsoClientSecrets.Decrypt(encryption, connection),
        SignOnUrl = connection.SignOnUrl,
        Certificate = connection.Certificate,
        Domains = [.. connection.Domains.Select(domain => domain.Domain)],
        Enabled = connection.Enabled,
        UpdatedAt = connection.UpdatedAt,
    };
}

/// <summary>Who an organization's provider says the person signing in is.</summary>
/// <remarks><c>HostedDomain</c> is the Workspace domain Google names in <c>hd</c>.</remarks>
public sealed record SsoIdentity(string Subject, string? Email, string? GivenName, string? FamilyName, string? Locale, string? HostedDomain = null);

/// <summary>
/// Finds or makes the account of someone an organization's provider signed in. Later sign-ins find it by the link the
/// first one made; the first needs an address in the connection's domains, and links the account with that address or
/// creates one, verified, as the provider vouches for the address. When the address of the account was not verified
/// yet, the link is the first proof that the person owns it, as a password reset is: accounts at other providers linked
/// before are unlinked and its sessions end. The account joins the organization if it is not a member yet.
/// </summary>
public sealed class SsoSignInHandler(
    UserManager<User> userManager,
    ExternalLogins logins,
    RegisterExternalUserHandler register,
    IUserSessions sessions,
    IKimlikDbContext context,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<User>> HandleAsync(SsoProvider connection, SsoIdentity identity, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!connection.IsOrganizationAccount(identity.HostedDomain))
        {
            return AccountErrors.AddressOutsideConnection;
        }

        var login = new ExternalLogin(connection.LoginProvider, identity.Subject, connection.Name);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        var user = await userManager.FindByLoginAsync(login.Provider, login.ProviderKey);
        if (user is null)
        {
            if (!connection.Covers(identity.Email))
            {
                return AccountErrors.AddressOutsideConnection;
            }

            var found = await userManager.FindByEmailAsync(identity.Email!) is { } existing
                ? await LinkAsync(existing, login, cancellationToken)
                : await register.HandleAsync(
                    new RegisterExternalUserCommand(login, identity.Email!, EmailVerified: true, identity.GivenName, identity.FamilyName, identity.Locale, returnUrl)
                    {
                        VouchedByOrganization = true,
                    },
                    cancellationToken);
            if (found.IsFailure)
            {
                return found.Error;
            }

            user = found.Value;
        }

        if (!await context.Memberships.AnyAsync(membership => membership.OrganizationId == connection.OrganizationId && membership.UserId == user.Id, cancellationToken))
        {
            context.Memberships.Add(Membership.Create(connection.OrganizationId, user.Id, timeProvider.GetUtcNow()));
            auditLog.Record(
                AuditActions.MembershipCreated,
                AuditSubject.User(user.Id),
                new Dictionary<string, object?> { ["roles"] = Array.Empty<string>(), ["sso_connection"] = connection.Id },
                AuditActor.User(user.Id),
                connection.OrganizationId);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    private async Task<Result<User>> LinkAsync(User user, ExternalLogin login, CancellationToken cancellationToken)
    {
        if ((await userManager.GetLoginsAsync(user)).Any(existing => existing.LoginProvider == login.Provider))
        {
            // The provider knows the person by another subject now, such as after their account there was made again.
            return AccountErrors.ProviderAlreadyLinked;
        }

        if (!user.EmailConfirmed)
        {
            foreach (var earlier in await userManager.GetLoginsAsync(user))
            {
                await userManager.RemoveLoginAsync(user, earlier.LoginProvider, earlier.ProviderKey);
                auditLog.Record(
                    AuditActions.UserLoginUnlinked,
                    AuditSubject.User(user.Id),
                    new Dictionary<string, object?> { ["provider"] = earlier.LoginProvider, ["reason"] = "sso" },
                    AuditActor.User(user.Id));
            }

            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
            await userManager.UpdateSecurityStampAsync(user);
            await sessions.RevokeAllAsync(user.Id, cancellationToken);
        }

        var added = await logins.AddAsync(user, login);
        if (added.IsFailure)
        {
            return added.Error;
        }

        auditLog.Record(AuditActions.UserLoginLinked, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["provider"] = login.Provider }, AuditActor.User(user.Id));
        return user;
    }
}
