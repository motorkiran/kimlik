using Kimlik.Domain.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class LoginChallengeConfiguration : IEntityTypeConfiguration<LoginChallenge>
{
    public void Configure(EntityTypeBuilder<LoginChallenge> builder)
    {
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();
        builder.Property(challenge => challenge.ChallengeHash).HasMaxLength(64);
        builder.Property(challenge => challenge.BrowserHash).HasMaxLength(64);
        builder.Property(challenge => challenge.ClientId).HasMaxLength(256);
        builder.Property(challenge => challenge.ReturnUrl).HasMaxLength(8192);
        builder.Property(challenge => challenge.LoginHint).HasMaxLength(256);
        builder.Property(challenge => challenge.Prompt).HasMaxLength(64);
        builder.Property(challenge => challenge.RequestedStrength).HasConversion<string>().HasMaxLength(32);
        builder.Property(challenge => challenge.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(challenge => challenge.DecidedBy).HasMaxLength(256);
        builder.Property(challenge => challenge.RejectionReason).HasMaxLength(256);
        builder.HasIndex(challenge => challenge.ChallengeHash).IsUnique();
        builder.HasIndex(challenge => challenge.ExpiresAt);
    }
}
