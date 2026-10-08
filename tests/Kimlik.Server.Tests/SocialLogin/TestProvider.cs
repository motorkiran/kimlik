using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Kimlik.Server.SocialLogin;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using OpenIddict.Client;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>What the test provider says about the person who signs in there.</summary>
internal sealed record TestProfile(string Subject, string? Email, bool EmailVerified = true, string? GivenName = "Grace", string? FamilyName = "Hopper")
{
    public static TestProfile New(string? email = null, bool emailVerified = true) =>
        new($"subject-{Guid.NewGuid():N}", email ?? $"social-{Guid.NewGuid():N}@example.com", emailVerified);
}

/// <summary>
/// An OAuth 2.0 provider for the tests, registered with Kimlik like Google or GitHub. The browser never reaches it:
/// tests read where Kimlik sends the browser, and come back with a code that <see cref="Issue"/> made. Kimlik's calls
/// to its token and user info endpoints are answered in process.
/// </summary>
internal sealed class TestProvider : IAsyncDisposable
{
    public const string Name = "test";
    public const string DisplayName = "Test Provider";

    public static readonly Uri Issuer = new("https://provider.test/");

    private readonly ConcurrentDictionary<string, TestProfile> _profiles = new();

    /// <summary>The Kimlik host that offers sign-in with this provider.</summary>
    public WebApplicationFactory<Program> Kimlik { get; private set; } = null!;

    private static readonly SemaphoreSlim Starting = new(1, 1);
    private static TestProvider? _shared;

    /// <summary>The Kimlik host with the test provider and default settings that most tests share.</summary>
    public static async Task<TestProvider> SharedAsync(KimlikServerFixture server)
    {
        await Starting.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            // The shared fixture disposes the hosts derived from it.
            return _shared ??= await StartAsync(server);
        }
        finally
        {
            Starting.Release();
        }
    }

    /// <summary>A Kimlik host that offers sign-in with the test provider, whose addresses it trusts unless told not to.</summary>
    public static async Task<TestProvider> StartAsync(KimlikServerFixture server, bool trustEmail = true, IDictionary<string, string?>? configuration = null)
    {
        var provider = new TestProvider();
        var kimlik = server.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()))
            .ConfigureTestServices(services =>
            {
                services.AddOpenIddict().AddClient(options => options.AddRegistration(Registration(trustEmail)));
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handler => handler.AdditionalHandlers.Add(new Endpoints(provider))));
            }));

        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        provider.Kimlik = kimlik;
        return provider;
    }

    public ValueTask DisposeAsync() => Kimlik.DisposeAsync();

    /// <summary>A code to come back to Kimlik with, for the person in <paramref name="profile"/>.</summary>
    public string Issue(TestProfile profile)
    {
        var code = Guid.NewGuid().ToString("N");
        _profiles[code] = profile;
        return code;
    }

    /// <summary>Follows Kimlik to the provider and back, as the person in <paramref name="profile"/>.</summary>
    public async Task<HttpResponseMessage> SignInAsync(Browser browser, HttpResponseMessage toProvider, TestProfile profile)
    {
        var authorize = toProvider.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect to the provider, got {(int)toProvider.StatusCode}.");
        authorize.GetLeftPart(UriPartial.Path).ShouldBe(new Uri(Issuer, "authorize").ToString());

        var query = QueryHelpers.ParseQuery(authorize.Query);
        var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?>
        {
            ["code"] = Issue(profile),
            ["state"] = query["state"].ToString(),
        });

        return await browser.GetAsync(callback);
    }

    private static OpenIddictClientRegistration Registration(bool trustEmail)
    {
        var registration = new OpenIddictClientRegistration
        {
            RegistrationId = Name,
            ProviderName = Name,
            ProviderDisplayName = DisplayName,
            Issuer = Issuer,
            ClientId = "kimlik",
            ClientSecret = "provider-secret",
            RedirectUri = new Uri($"signin/external/callback/{Name}", UriKind.Relative),
            Configuration = new()
            {
                Issuer = Issuer,
                AuthorizationEndpoint = new Uri(Issuer, "authorize"),
                TokenEndpoint = new Uri(Issuer, "token"),
                UserInfoEndpoint = new Uri(Issuer, "userinfo"),
            },
        };

        registration.Configuration.GrantTypesSupported.Add(GrantTypes.AuthorizationCode);
        registration.Configuration.ResponseTypesSupported.Add(ResponseTypes.Code);
        registration.Configuration.CodeChallengeMethodsSupported.Add(CodeChallengeMethods.Sha256);
        registration.Configuration.TokenEndpointAuthMethodsSupported.Add(ClientAuthenticationMethods.ClientSecretPost);
        registration.Scopes.UnionWith([Scopes.Email, Scopes.Profile]);
        registration.Properties[ExternalProviders.TrustEmailProperty] = trustEmail;
        return registration;
    }

    /// <summary>The provider's token and user info endpoints, in front of Kimlik's outgoing HTTP calls.</summary>
    private sealed class Endpoints(TestProvider provider) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != Issuer.Host)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            if (request.RequestUri.AbsolutePath == "/token")
            {
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                return provider._profiles.ContainsKey(form["code"].ToString())
                    ? Json(new Dictionary<string, object> { ["access_token"] = form["code"].ToString(), ["token_type"] = "Bearer", ["expires_in"] = 3600 })
                    : Json(new Dictionary<string, object> { ["error"] = "invalid_grant" }, HttpStatusCode.BadRequest);
            }

            if (request.RequestUri.AbsolutePath == "/userinfo"
                && request.Headers.Authorization?.Parameter is { } token
                && provider._profiles.TryGetValue(token, out var profile))
            {
                var claims = new Dictionary<string, object> { ["sub"] = profile.Subject, ["email_verified"] = profile.EmailVerified };
                AddIfSet(claims, "email", profile.Email);
                AddIfSet(claims, "given_name", profile.GivenName);
                AddIfSet(claims, "family_name", profile.FamilyName);
                return Json(claims);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static void AddIfSet(Dictionary<string, object> claims, string name, string? value)
        {
            if (value is not null)
            {
                claims[name] = value;
            }
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body) };
    }
}

internal static class TestProviders
{
    public static Task<TestProvider> TestProviderAsync(this KimlikServerFixture server) => TestProvider.SharedAsync(server);
}
