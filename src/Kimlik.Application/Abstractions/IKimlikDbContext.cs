using Kimlik.Domain.Access;
using Kimlik.Domain.ApiKeys;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Plans;
using Kimlik.Domain.Users;
using Kimlik.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Application.Abstractions;

/// <summary>The unit of work for use cases. EF Core's <see cref="DbSet{TEntity}"/> already is the repository.</summary>
public interface IKimlikDbContext
{
    DbSet<User> Users { get; }

    DbSet<SessionClient> SessionClients { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<Role> Roles { get; }

    DbSet<RolePermission> RolePermissions { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<ClientRole> ClientRoles { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Membership> Memberships { get; }

    DbSet<MembershipRole> MembershipRoles { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<Feature> Features { get; }

    DbSet<Plan> Plans { get; }

    DbSet<Subscription> Subscriptions { get; }

    DbSet<ApiKey> ApiKeys { get; }

    DbSet<ApiKeyPermission> ApiKeyPermissions { get; }

    DbSet<WebhookEndpoint> WebhookEndpoints { get; }

    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    /// <summary>OpenID Connect clients, for queries. Change them through <c>IOpenIddictApplicationManager</c>, which validates them and hashes secrets.</summary>
    IQueryable<OpenIddictEntityFrameworkCoreApplication<Guid>> Applications { get; }

    /// <summary>Scopes, for queries. Change them through <c>IOpenIddictScopeManager</c>.</summary>
    IQueryable<OpenIddictEntityFrameworkCoreScope<Guid>> Scopes { get; }

    /// <summary>The audit trail, for queries. Record events through <see cref="IAuditLog"/>.</summary>
    IQueryable<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a transaction, or joins the one already in progress, which then decides the outcome.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
