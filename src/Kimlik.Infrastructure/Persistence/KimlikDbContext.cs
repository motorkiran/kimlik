using Kimlik.Application.Abstractions;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Outbox;
using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kimlik.Infrastructure.Persistence;

public sealed class KimlikDbContext(DbContextOptions<KimlikDbContext> options)
    : IdentityUserContext<User, Guid>(options), IKimlikDbContext, IDataProtectionKeyContext
{
    public const string Schema = "kimlik";

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
