using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using OpenIddict.Server;
using OpenIddict.Validation;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Gives the OpenID Connect server every usable key: all of them validate, decrypt and appear in JWKS,
/// and the active ones sign and encrypt new tokens. The validation of Kimlik's own API gets the same keys, and so
/// does the client that signs in with other providers, for its state tokens.
/// </summary>
internal sealed class ConfigureTokenKeyCredentials(TokenKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictServerOptions>, IPostConfigureOptions<OpenIddictValidationOptions>,
        IConfigureOptions<OpenIddictClientOptions>, IPostConfigureOptions<OpenIddictClientOptions>
{
    public void Configure(OpenIddictServerOptions options) => AddCredentials(options.SigningCredentials, options.EncryptionCredentials);

    public void Configure(OpenIddictClientOptions options) => AddCredentials(options.SigningCredentials, options.EncryptionCredentials);

    /// <summary>
    /// OpenIddict signs and encrypts with the first credentials in each list, but its own post-configuration
    /// sorts keys that are not X.509 certificates without a stable order. This runs after it (it is registered
    /// after the server) and moves the active keys back to the front.
    /// </summary>
    public void PostConfigure(string? name, OpenIddictServerOptions options) => MoveActiveToFront(options.SigningCredentials, options.EncryptionCredentials);

    /// <inheritdoc cref="PostConfigure(string?, OpenIddictServerOptions)"/>
    public void PostConfigure(string? name, OpenIddictClientOptions options) => MoveActiveToFront(options.SigningCredentials, options.EncryptionCredentials);

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

    private void AddCredentials(List<SigningCredentials> signing, List<EncryptingCredentials> encryption)
    {
        var keys = keyRing.Current ?? throw new InvalidOperationException("The token keys have not been loaded yet.");

        signing.AddRange(keys.SigningKeys.Select(key => new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256)));
        encryption.AddRange(keys.EncryptionKeys.Select(EncryptingCredentials));
    }

    private void MoveActiveToFront(List<SigningCredentials> signing, List<EncryptingCredentials> encryption)
    {
        if (keyRing.Current is not { } keys)
        {
            return;
        }

        MoveToFront(signing, credentials => credentials.Key.KeyId == keys.ActiveKey(TokenKeyUse.Signing).KeyId);
        MoveToFront(encryption, credentials => credentials.Key.KeyId == keys.ActiveKey(TokenKeyUse.Encryption).KeyId);
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
