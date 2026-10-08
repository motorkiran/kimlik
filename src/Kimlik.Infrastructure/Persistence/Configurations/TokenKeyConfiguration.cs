using Kimlik.Infrastructure.Security.TokenKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class TokenKeyConfiguration : IEntityTypeConfiguration<TokenKey>
{
    public void Configure(EntityTypeBuilder<TokenKey> builder)
    {
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.KeyId).HasMaxLength(TokenKey.KeyIdMaxLength);
        builder.Property(key => key.Use).HasConversion<string>().HasMaxLength(16);
        builder.Property(key => key.Algorithm).HasMaxLength(TokenKey.AlgorithmMaxLength);

        builder.HasIndex(key => key.KeyId).IsUnique();
    }
}
