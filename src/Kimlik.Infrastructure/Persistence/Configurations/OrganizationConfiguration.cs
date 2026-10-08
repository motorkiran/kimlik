using Kimlik.Domain.Access;
using Kimlik.Domain.Organizations;
using Kimlik.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(organization => organization.Id).ValueGeneratedNever();
        builder.Property(organization => organization.Name).HasMaxLength(Organization.NameMaxLength);
        builder.Property(organization => organization.Slug).HasMaxLength(Organization.SlugMaxLength);
        builder.HasIndex(organization => organization.Slug).IsUnique();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.Property(membership => membership.Id).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(membership => membership.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(membership => new { membership.OrganizationId, membership.UserId }).IsUnique();
        builder.HasIndex(membership => membership.UserId);

        builder.HasMany(membership => membership.Roles).WithOne().HasForeignKey(link => link.MembershipId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(membership => membership.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.HasKey(link => new { link.MembershipId, link.RoleId });
        builder.HasOne<Role>().WithMany().HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(link => link.RoleId);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(invitation => invitation.Id).ValueGeneratedNever();
        builder.Property(invitation => invitation.Email).HasMaxLength(Invitation.EmailMaxLength);
        builder.Property(invitation => invitation.NormalizedEmail).HasMaxLength(Invitation.EmailMaxLength);
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64);
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasOne<Organization>().WithMany().HasForeignKey(invitation => invitation.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => new { invitation.OrganizationId, invitation.NormalizedEmail });
        builder.HasIndex(invitation => invitation.NormalizedEmail);

        builder.HasMany(invitation => invitation.Roles).WithOne().HasForeignKey(link => link.InvitationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(invitation => invitation.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvitationRoleConfiguration : IEntityTypeConfiguration<InvitationRole>
{
    public void Configure(EntityTypeBuilder<InvitationRole> builder)
    {
        builder.HasKey(link => new { link.InvitationId, link.RoleId });
        builder.HasOne<Role>().WithMany().HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
