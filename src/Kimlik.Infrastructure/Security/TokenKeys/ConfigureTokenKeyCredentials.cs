using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Gives the OpenID Connect server every usable key: all of them validate, decrypt and appear in JWKS,
/// and the active ones sign and encrypt new tokens.
/// </summary>
internal sealed class ConfigureTokenKeyCredentials(TokenKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        var keys = keyRing.Current ?? throw new InvalidOperationException("The token keys have not been loaded yet.");

        foreach (var key in keys.SigningKeys)
        {
            options.SigningCredentials.Add(new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256));
        }

        foreach (var key in keys.EncryptionKeys)
        {
            options.EncryptionCredentials.Add(new EncryptingCredentials(
                key.SecurityKey, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }

    /// <summary>
    /// OpenIddict signs and encrypts with the first credentials in each list, but its own post-configuration
    /// sorts keys that are not X.509 certificates without a stable order. This runs after it (it is registered
    /// after the server) and moves the active keys back to the front.
    /// </summary>
    public void PostConfigure(string? name, OpenIddictServerOptions options)
    {
        if (keyRing.Current is not { } keys)
        {
            return;
        }

        MoveToFront(options.SigningCredentials, credentials => credentials.Key.KeyId == keys.ActiveKey(TokenKeyUse.Signing).KeyId);
        MoveToFront(options.EncryptionCredentials, credentials => credentials.Key.KeyId == keys.ActiveKey(TokenKeyUse.Encryption).KeyId);
    }

    private static void MoveToFront<T>(List<T> credentials, Predicate<T> isActive)
    {
        var index = credentials.FindIndex(isActive);
        if (index > 0)
        {
            var active = credentials[index];
            credentials.RemoveAt(index);
            credentials.Insert(0, active);
        }
    }
}
