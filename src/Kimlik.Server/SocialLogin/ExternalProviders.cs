using Microsoft.Extensions.Options;
using OpenIddict.Client;

namespace Kimlik.Server.SocialLogin;

/// <summary>A provider people can sign in with.</summary>
/// <param name="Name">Kimlik's name for it, such as <c>google</c>, in URLs, linked logins and the APIs.</param>
/// <param name="DisplayName">Its name for people, such as <c>Google</c>.</param>
/// <param name="TrustEmail">Whether addresses it verified count as verified in Kimlik.</param>
public sealed record ExternalProvider(string Name, string DisplayName, bool TrustEmail);

/// <summary>The providers people can sign in with: the OpenIddict client registrations that name a provider.</summary>
public sealed class ExternalProviders(IOptionsMonitor<OpenIddictClientOptions> client)
{
    public const string Google = "google";
    public const string Microsoft = "microsoft";
    public const string Apple = "apple";
    public const string GitHub = "github";

    /// <summary>
    /// The sign-in property that marks connecting an account from the account pages, holding the ID of the user who
    /// asked to.
    /// </summary>
    public const string LinkingUserProperty = "kimlik:linking_user";

    /// <summary>The registration property, a <see cref="bool"/>, that says whether the provider's addresses are trusted.</summary>
    public const string TrustEmailProperty = "kimlik:trust_email";

    public IReadOnlyList<ExternalProvider> All =>
    [
        .. client.CurrentValue.Registrations
            .Where(registration => registration.ProviderName is not null)
            .Select(registration => new ExternalProvider(
                registration.ProviderName!,
                registration.ProviderDisplayName ?? registration.ProviderName!,
                registration.Properties.TryGetValue(TrustEmailProperty, out var trust) && trust is true)),
    ];

    public ExternalProvider? Find(string? name) => All.FirstOrDefault(provider => provider.Name == name);
}
