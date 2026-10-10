using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.Property(user => user.GivenName).HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.FamilyName).HasMaxLength(User.NameMaxLength);
        builder.Property(user => user.Locale).HasMaxLength(User.LocaleMaxLength);
        builder.Property(user => user.PictureUrl).HasMaxLength(User.PictureUrlMaxLength);
        builder.Property(user => user.TimeZone).HasMaxLength(User.TimeZoneMaxLength);
        builder.Property(user => user.PhoneNumber).HasMaxLength(User.PhoneNumberMaxLength);
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(user => user.PublicMetadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(user => user.PrivateMetadata).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");

        builder.HasIndex(user => user.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");

        // Identity only checks email uniqueness in code; the unique index closes the race between two sign-ups.
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");

        // A verified number belongs to one account, which signing in with texts finds by it.
        builder.HasIndex(user => user.PhoneNumber).IsUnique().HasFilter("phone_number_confirmed").HasDatabaseName("ix_users_phone_number");
    }
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
}

internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<IdentityUserPasskey<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserPasskey<Guid>> builder) => builder.ToTable("user_passkeys");
}
