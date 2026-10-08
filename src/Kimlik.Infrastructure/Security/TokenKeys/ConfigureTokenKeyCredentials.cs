using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Validation;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Gives the OpenID Connect server every usable key: all of them validate, decrypt and appear in JWKS,
/// and the active ones sign and encrypt new tokens. The validation of Kimlik's own API gets the same keys.
/// </summary>
internal sealed class ConfigureTokenKeyCredentials(TokenKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictValidationOptions>
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
            options.EncryptionCredentials.Add(EncryptingCredentials(key));
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

    /// <summary>
    /// The local validation copies its keys from the server options, but both options are rebuilt when the
    /// keys change, in no particular order, so the copy may be of the previous keys. The key ring is the
    /// source of truth for both.
    /// </summary>
    public void PostConfigure(string? name, OpenIddictValidationOptions options)
    {
        if (keyRing.Current is not { } keys || options.Configuration is null)
        {
            return;
        }

        options.Configuration.SigningKeys.Clear();
        options.Configuration.SigningKeys.AddRange(keys.SigningKeys.Select(key => key.SecurityKey));

        options.EncryptionCredentials.Clear();
        options.EncryptionCredentials.AddRange(keys.EncryptionKeys.Select(EncryptingCredentials));
    }

    private static EncryptingCredentials EncryptingCredentials(LoadedTokenKey key) =>
        new(key.SecurityKey, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512);

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
