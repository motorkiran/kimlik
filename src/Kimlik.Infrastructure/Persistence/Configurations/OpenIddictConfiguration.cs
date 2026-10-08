using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace Kimlik.Infrastructure.Persistence.Configurations;

// OpenIddict maps its entities itself; these configurations only give the tables Kimlik's names.

internal sealed class OidcApplicationConfiguration : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreApplication<Guid>>
{
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreApplication<Guid>> builder) => builder.ToTable("oidc_applications");
}

internal sealed class OidcAuthorizationConfiguration : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreAuthorization<Guid>>
{
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreAuthorization<Guid>> builder) => builder.ToTable("oidc_authorizations");
}

internal sealed class OidcScopeConfiguration : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreScope<Guid>>
{
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreScope<Guid>> builder) => builder.ToTable("oidc_scopes");
}

internal sealed class OidcTokenConfiguration : IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreToken<Guid>>
{
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreToken<Guid>> builder) => builder.ToTable("oidc_tokens");
}
