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
    ApiKeyCache cache,
    IOptions<KimlikOptions> kimlik)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>The API key in the request, if its bearer token is one.</summary>
    public static string? ApiKeyOf(HttpRequest request) =>
        request.Headers.Authorization.ToString() is var header && header.StartsWith(BearerPrefix + ApiKeyFormat.Prefix, StringComparison.Ordinal)
            ? header[BearerPrefix.Length..].Trim()
            : null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ApiKeyOf(Request) is not { } key)
        {
            return AuthenticateResult.NoResult();
        }

        // Made-up values are turned away here, without a call to Kimlik or an entry in the cache.
        if (!ApiKeyFormat.IsWellFormed(key))
        {
            return AuthenticateResult.Fail("The API key is not valid.");
        }

        // The cache holds hashes of keys, never keys.
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
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
                cache.Set(cacheKey, verification, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = kimlik.Value.ApiKeys.CacheDuration, Size = 1 });
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

        if (verification.CustomEntitlements)
        {
            identity.AddClaim(new Claim(KimlikClaimTypes.CustomEntitlements, "true", ClaimValueTypes.Boolean));
        }

        return new ClaimsPrincipal(identity);
    }

    [LoggerMessage(LogLevel.Warning, "Kimlik could not verify an API key")]
    private static partial void LogVerificationFailed(ILogger logger, Exception exception);
}
