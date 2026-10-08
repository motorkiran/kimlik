using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Plans;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Outbox;
using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Infrastructure.Persistence;

public sealed class KimlikDbContext(DbContextOptions<KimlikDbContext> options)
    : IdentityUserContext<User, Guid>(options), IKimlikDbContext, IDataProtectionKeyContext
{
    public const string Schema = "kimlik";

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<ClientRole> ClientRoles => Set<ClientRole>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Feature> Features => Set<Feature>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public IQueryable<OpenIddictEntityFrameworkCoreApplication<Guid>> Applications => Set<OpenIddictEntityFrameworkCoreApplication<Guid>>();

    public IQueryable<OpenIddictEntityFrameworkCoreScope<Guid>> Scopes => Set<OpenIddictEntityFrameworkCoreScope<Guid>>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    IQueryable<AuditEvent> IKimlikDbContext.AuditEvents => AuditEvents;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<TokenKey> TokenKeys => Set<TokenKey>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Starts a transaction, or joins the one in progress: a use case that runs as part of a larger one, such as
    /// applying a provisioning file, then commits or rolls back with it.
    /// </summary>
    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.CurrentTransaction is { } current ? new JoinedTransaction(current) : await Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict<Guid>();
        builder.ApplyConfigurationsFromAssembly(typeof(KimlikDbContext).Assembly);
    }
}
