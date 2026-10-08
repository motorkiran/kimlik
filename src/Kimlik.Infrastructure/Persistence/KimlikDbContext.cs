using Microsoft.EntityFrameworkCore;

namespace Kimlik.Infrastructure.Persistence;

public sealed class KimlikDbContext(DbContextOptions<KimlikDbContext> options) : DbContext(options)
{
    public const string Schema = "kimlik";

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
}
