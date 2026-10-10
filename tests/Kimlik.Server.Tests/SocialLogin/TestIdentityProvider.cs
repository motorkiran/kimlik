using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kimlik.Server.Tests.SocialLogin;

/// <summary>What an organization's provider says about the person who signs in there.</summary>
internal sealed record WorkProfile(string Subject, string? Email, string[] Methods)
{
    public static WorkProfile New(string email, params string[] methods) => new($"employee-{Guid.NewGuid():N}", email, methods.Length > 0 ? methods : ["pwd"]);
}

/// <summary>
/// An organization's OpenID Connect provider for the tests, such as Entra ID or Okta, which SSO connections name by its
/// issuer. Kimlik discovers its endpoints and keys and redeems codes for signed ID tokens in process; the browser never
/// reaches it: tests read where Kimlik sends the browser, and <see cref="SignInAsync"/> comes back with a code.
/// </summary>
internal sealed class TestIdentityProvider : IAsyncDisposable
{
    public const string Issuer = "https://idp.test/acme";
    public const string ClientId = "kimlik-at-acme";
    public const string ClientSecret = "secret at the provider";

    private const string KeyId = "idp-key";

    private readonly ConcurrentDictionary<string, (WorkProfile Profile, string? Nonce)> _codes = new();
    private readonly RSA _key = RSA.Create(2048);

    /// <summary>The Kimlik host whose outgoing calls reach the provider. Registration on it is by invitation only.</summary>
    public WebApplicationFactory<Program> Kimlik { get; private set; } = null!;

    /// <summary>The addresses the provider sent as <c>login_hint</c>, in order.</summary>
    public ConcurrentQueue<string?> LoginHints { get; } = new();

    private static readonly SemaphoreSlim Starting = new(1, 1);
    private static TestIdentityProvider? _shared;

    public static async Task<TestIdentityProvider> SharedAsync(KimlikServerFixture server)
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

    private static async Task<TestIdentityProvider> StartAsync(KimlikServerFixture server)
    {
        var provider = new TestIdentityProvider();
        var kimlik = server.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kimlik:Accounts:Registration"] = "InviteOnly",
            }))
            .ConfigureTestServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.AdditionalHandlers.Add(new Endpoints(provider))))));

        await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
        provider.Kimlik = kimlik;
        return provider;
    }

    public ValueTask DisposeAsync()
    {
        _key.Dispose();
        return Kimlik.DisposeAsync();
    }

    /// <summary>Follows Kimlik to the provider and back, as the person in <paramref name="profile"/>.</summary>
    public async Task<HttpResponseMessage> SignInAsync(Browser browser, HttpResponseMessage toProvider, WorkProfile profile)
    {
        var authorize = toProvider.Headers.Location ?? throw new InvalidOperationException($"Expected a redirect to the provider, got {(int)toProvider.StatusCode}.");
        authorize.GetLeftPart(UriPartial.Path).ShouldBe($"{Issuer}/authorize");

        var query = QueryHelpers.ParseQuery(authorize.Query);
        query["client_id"].ToString().ShouldBe(ClientId);
        LoginHints.Enqueue(query.TryGetValue("login_hint", out var hint) ? hint.ToString() : null);

        var code = Guid.NewGuid().ToString("N");
        _codes[code] = (profile, query["nonce"].ToString());
        var callback = QueryHelpers.AddQueryString(query["redirect_uri"].ToString(), new Dictionary<string, string?> { ["code"] = code, ["state"] = query["state"].ToString() });
        return await browser.GetAsync(callback);
    }

    private string IdentityToken(WorkProfile profile, string? nonce)
    {
        var claims = new Dictionary<string, object> { ["sub"] = profile.Subject, ["amr"] = profile.Methods, ["given_name"] = "Grace", ["family_name"] = "Hopper" };
        if (profile.Email is not null)
        {
            claims["email"] = profile.Email;
        }

        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_key) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>The provider's discovery document, keys and token endpoint, in front of Kimlik's outgoing HTTP calls.</summary>
    private sealed class Endpoints(TestIdentityProvider provider) : DelegatingHandler
    {
        private static readonly Dictionary<string, object> Configuration = new()
        {
            ["issuer"] = Issuer,
            ["authorization_endpoint"] = $"{Issuer}/authorize",
            ["token_endpoint"] = $"{Issuer}/token",
            ["jwks_uri"] = $"{Issuer}/keys",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["scopes_supported"] = new[] { "openid", "email", "profile" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_basic", "client_secret_post" },
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { Host: "idp.test" } uri)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            return uri.AbsolutePath switch
            {
                "/acme/.well-known/openid-configuration" => Json(Configuration),
                "/acme/keys" => Json(TestKeys.KeySet(provider._key, KeyId)),
                "/acme/token" => await RedeemAsync(request, cancellationToken),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private async Task<HttpResponseMessage> RedeemAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (!Authenticated(request, form) || !provider._codes.TryRemove(form["code"].ToString(), out var issued))
            {
                return Json(new Dictionary<string, object> { ["error"] = "invalid_grant" }, HttpStatusCode.BadRequest);
            }

            return Json(new Dictionary<string, object>
            {
                ["access_token"] = Guid.NewGuid().ToString("N"),
                ["token_type"] = "Bearer",
                ["expires_in"] = 300,
                ["id_token"] = provider.IdentityToken(issued.Profile, issued.Nonce),
            });
        }

        /// <summary>Whether Kimlik sent the client secret, in the Authorization header or the form.</summary>
        private static bool Authenticated(HttpRequestMessage request, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> form)
        {
            if (request.Headers.Authorization is { Scheme: "Basic", Parameter: { } parameter })
            {
                var pair = Encoding.UTF8.GetString(Convert.FromBase64String(parameter)).Split(':', 2);
                return Uri.UnescapeDataString(pair[0]) == ClientId && Uri.UnescapeDataString(pair[1].Replace('+', ' ')) == ClientSecret;
            }

            return form["client_id"] == ClientId && form["client_secret"] == ClientSecret;
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body, mediaType: new MediaTypeHeaderValue("application/json")) };
    }
}
