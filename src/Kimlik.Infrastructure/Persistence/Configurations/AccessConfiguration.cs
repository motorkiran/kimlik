using Kimlik.Domain.Access;
using Kimlik.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.Property(permission => permission.Id).ValueGeneratedNever();
        builder.Property(permission => permission.Key).HasMaxLength(AccessKeys.PermissionKeyMaxLength);
        builder.Property(permission => permission.Description).HasMaxLength(Permission.DescriptionMaxLength);
        builder.HasIndex(permission => permission.Key).IsUnique();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(role => role.Id).ValueGeneratedNever();
        builder.Property(role => role.Key).HasMaxLength(AccessKeys.RoleKeyMaxLength);
        builder.Property(role => role.Name).HasMaxLength(Role.NameMaxLength);
        builder.Property(role => role.Description).HasMaxLength(Role.DescriptionMaxLength);
        builder.Property(role => role.Scope).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(role => role.Key).IsUnique();

        builder.HasMany(role => role.Permissions).WithOne().HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(link => new { link.RoleId, link.PermissionId });
        builder.HasOne<Permission>().WithMany().HasForeignKey(link => link.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasKey(assignment => new { assignment.UserId, assignment.RoleId });
        builder.HasOne<User>().WithMany().HasForeignKey(assignment => assignment.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(assignment => assignment.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(assignment => assignment.RoleId);
    }
}

internal sealed class ClientRoleConfiguration : IEntityTypeConfiguration<ClientRole>
{
    public void Configure(EntityTypeBuilder<ClientRole> builder)
    {
        builder.HasKey(assignment => new { assignment.ApplicationId, assignment.RoleId });
        builder.HasOne<OpenIddictEntityFrameworkCoreApplication<Guid>>().WithMany()
            .HasForeignKey(assignment => assignment.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(assignment => assignment.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(assignment => assignment.RoleId);
    }
}
