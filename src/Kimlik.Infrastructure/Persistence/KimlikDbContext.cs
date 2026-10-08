using Kimlik.Application.Abstractions;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
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

    public IQueryable<OpenIddictEntityFrameworkCoreApplication<Guid>> Applications => Set<OpenIddictEntityFrameworkCoreApplication<Guid>>();

    public IQueryable<OpenIddictEntityFrameworkCoreScope<Guid>> Scopes => Set<OpenIddictEntityFrameworkCoreScope<Guid>>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<TokenKey> TokenKeys => Set<TokenKey>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict<Guid>();
        builder.ApplyConfigurationsFromAssembly(typeof(KimlikDbContext).Assembly);
    }
}
