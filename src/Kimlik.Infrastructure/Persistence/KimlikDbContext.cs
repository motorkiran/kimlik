using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Infrastructure.Outbox;
using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Infrastructure.Persistence;

public sealed class KimlikDbContext(DbContextOptions<KimlikDbContext> options)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "kimlik";

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<TokenKey> TokenKeys => Set<TokenKey>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict<Guid>();
        builder.ApplyConfigurationsFromAssembly(typeof(KimlikDbContext).Assembly);
    }
}
