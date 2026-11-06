using Kimlik.Infrastructure.Security.Dpop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class UsedDpopProofConfiguration : IEntityTypeConfiguration<UsedDpopProof>
{
    public void Configure(EntityTypeBuilder<UsedDpopProof> builder)
    {
        builder.HasKey(proof => proof.Hash);
        builder.Property(proof => proof.Hash).HasMaxLength(UsedDpopProof.HashLength);
        builder.HasIndex(proof => proof.ExpiresAt);
    }
}
