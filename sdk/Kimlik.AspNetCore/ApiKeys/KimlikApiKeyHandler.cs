using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Kimlik.Client;
using Kimlik.Contracts;
using Kimlik.Contracts.Management;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kimlik.AspNetCore.ApiKeys;

/// <summary>
/// Authenticates requests whose bearer token is a Kimlik API key, by asking Kimlik about the key and reusing the
/// answer briefly. The caller gets the claims an access token would give, so <see cref="KimlikUser"/>, permission and
/// feature requirements work the same.
/// </summary>
internal sealed partial class KimlikApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IServiceProvider services,
    IMemoryCache cache,
    IOptions<KimlikOptions> kimlik)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";
    private const string KeyPrefix = "kmk_";

    /// <summary>The API key in the request, if its bearer token is one.</summary>
    public static string? ApiKeyOf(HttpRequest request) =>
        request.Headers.Authorization.ToString() is var header && header.StartsWith(BearerPrefix + KeyPrefix, StringComparison.Ordinal)
            ? header[BearerPrefix.Length..].Trim()
            : null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ApiKeyOf(Request) is not { } key)
        {
            return AuthenticateResult.NoResult();
        }

        // The cache holds hashes of keys, never keys.
        var cacheKey = $"kimlik:api-key:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";
        if (!cache.TryGetValue(cacheKey, out ApiKeyVerificationResponse? verification))
        {
            try
            {
                verification = await services.GetRequiredService<KimlikClient>().ApiKeys.VerifyAsync(key, Context.RequestAborted);
            }
            catch (Exception exception) when (exception is HttpRequestException or KimlikApiException)
            {
                LogVerificationFailed(Logger, exception);
                return AuthenticateResult.Fail("Kimlik could not verify the API key.");
            }

            if (kimlik.Value.ApiKeys.CacheDuration > TimeSpan.Zero)
            {
                cache.Set(cacheKey, verification, kimlik.Value.ApiKeys.CacheDuration);
            }
        }

        return verification is { Active: true, Id: { } keyId }
            ? AuthenticateResult.Success(new AuthenticationTicket(PrincipalOf(verification, keyId), Scheme.Name))
            : AuthenticateResult.Fail("The API key is not valid.");
    }

    private ClaimsPrincipal PrincipalOf(ApiKeyVerificationResponse verification, Guid keyId)
    {
        var identity = new ClaimsIdentity(Scheme.Name, JwtRegisteredClaimNames.Name, KimlikClaimTypes.Roles);
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, (verification.UserId ?? keyId).ToString()));
        identity.AddClaim(new Claim(KimlikClaimTypes.ApiKeyId, keyId.ToString()));
        identity.AddClaims((verification.Permissions ?? []).Select(permission => new Claim(KimlikClaimTypes.Permissions, permission)));

        if (verification.OrganizationId is { } organizationId)
        {
            identity.AddClaim(new Claim(KimlikClaimTypes.OrganizationId, organizationId.ToString()));
        }

        if (verification.Plan is { } plan)
        {
            identity.AddClaim(new Claim(KimlikClaimTypes.Plan, plan));
        }

        return new ClaimsPrincipal(identity);
    }

    [LoggerMessage(LogLevel.Warning, "Kimlik could not verify an API key")]
    private static partial void LogVerificationFailed(ILogger logger, Exception exception);
}
