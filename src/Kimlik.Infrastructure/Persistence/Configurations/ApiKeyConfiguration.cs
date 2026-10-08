using Kimlik.Domain.Access;
using Kimlik.Domain.ApiKeys;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_api_keys_one_owner", "(user_id IS NULL) <> (organization_id IS NULL)"));
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.Name).HasMaxLength(ApiKey.NameMaxLength);
        builder.Property(key => key.Prefix).HasMaxLength(16);
        builder.Property(key => key.SecretHash).HasMaxLength(64);
        builder.Ignore(key => key.Owner);

        // Verification finds a key by the hash of the secret it is given.
        builder.HasIndex(key => key.SecretHash).IsUnique();
        builder.HasIndex(key => key.UserId);
        builder.HasIndex(key => key.OrganizationId);

        // An owner's keys go with it; an organization's keys outlive the member who created them.
        builder.HasOne<User>().WithMany().HasForeignKey(key => key.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(key => key.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(key => key.CreatedBy).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(key => key.Permissions).WithOne().HasForeignKey(link => link.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(key => key.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ApiKeyPermissionConfiguration : IEntityTypeConfiguration<ApiKeyPermission>
{
    public void Configure(EntityTypeBuilder<ApiKeyPermission> builder)
    {
        builder.HasKey(link => new { link.ApiKeyId, link.PermissionId });
        builder.HasOne<Permission>().WithMany().HasForeignKey(link => link.PermissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(link => link.PermissionId);
    }
}
