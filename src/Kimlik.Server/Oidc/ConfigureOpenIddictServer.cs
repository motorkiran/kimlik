using Kimlik.Server.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace Kimlik.Server.Oidc;

/// <summary>Applies Kimlik's settings to the OpenID Connect server when its options are built.</summary>
internal sealed class ConfigureOpenIddictServer(IOptions<ServerOptions> server, IOptions<TokenOptions> tokens)
    : IConfigureOptions<OpenIddictServerOptions>, IConfigureOptions<OpenIddictServerAspNetCoreOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        options.Issuer = server.Value.PublicUrl;
        options.AccessTokenLifetime = tokens.Value.AccessTokenLifetime;
        options.IdentityTokenLifetime = tokens.Value.IdentityTokenLifetime;
        options.AuthorizationCodeLifetime = tokens.Value.AuthorizationCodeLifetime;
        options.RefreshTokenLifetime = tokens.Value.RefreshTokenLifetime;
        options.RefreshTokenReuseLeeway = tokens.Value.RefreshTokenReuseLeeway;
    }

    public void Configure(OpenIddictServerAspNetCoreOptions options) =>
        options.DisableTransportSecurityRequirement = !server.Value.RequireHttps;
}
