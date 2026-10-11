using Kimlik.Application.Interactions;
using Kimlik.Domain.Interactions;
using Kimlik.Server.Hosting;
using Kimlik.Server.Oidc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;

namespace Kimlik.Server.Interactions;

/// <summary>
/// Hands sign-ins to the installation's login app (design §10.12): a challenge for the authorization request, bound to the
/// browser by a cookie, and the way back for a rejection.
/// </summary>
internal static class LoginApps
{
    /// <summary>
    /// The cookie that holds the browser's secret, which the challenges it was sent with are bound to. Authorization
    /// requests read it too, so it goes to the whole host, and only the host itself can set it when HTTPS is required.
    /// </summary>
    public static string BrowserCookie(ServerOptions server) => server.RequireHttps ? "__Host-kimlik.login_challenge" : "kimlik.login_challenge";

    /// <summary>Where the browser finishes a decided challenge.</summary>
    public const string CompletionPath = "/interaction/login";

    /// <summary>The parameter that a rejected challenge comes back to the authorization request with.</summary>
    private const string RejectedParameter = "kimlik_login_rejected";

    private static readonly TimeSpan RejectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Starts a challenge for the request and sends the browser to the login app with it.</summary>
    public static async Task<IActionResult> HandOverAsync(
        HttpContext http, OpenIddictRequest request, Uri loginUrl, string returnUrl, LoginChallenges challenges, ServerOptions server, CancellationToken cancellationToken)
    {
        // One secret per browser, so that challenges in several tabs all stay bound to it.
        var secret = http.Request.Cookies[BrowserCookie(server)] is { Length: 64 } existing ? existing : LoginChallenges.NewSecret();
        http.Response.Cookies.Append(BrowserCookie(server), secret, new CookieOptions
        {
            HttpOnly = true,
            Secure = server.RequireHttps || http.Request.IsHttps,
            // The login app sends the browser back with a top-level navigation, which carries a Lax cookie.
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = LoginChallenge.Lifetime,
            IsEssential = true,
        });

        var acr = request.AcrValues?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var strength = acr.Contains(AuthenticationContexts.PhishingResistant) ? RequestedStrength.PhishingResistant
            : acr.Contains(AuthenticationContexts.MultiFactor) ? RequestedStrength.MultiFactor
            : RequestedStrength.Any;
        var challenge = await challenges.StartAsync(
            new LoginChallengeStart(request.ClientId!, returnUrl, request.GetScopes(), request.LoginHint is { Length: <= 256 } hint ? hint : null, request.Prompt, strength), secret, cancellationToken);
        return new RedirectResult(QueryHelpers.AddQueryString(loginUrl.AbsoluteUri, "login_challenge", challenge));
    }

    /// <summary>The authorization request to come back to after a rejection, carrying the login app's reason, protected.</summary>
    public static string Rejected(string returnUrl, string? reason, IDataProtectionProvider dataProtection, TimeProvider timeProvider) =>
        QueryHelpers.AddQueryString(returnUrl, RejectedParameter, ProtectorFor(dataProtection).Protect(reason ?? string.Empty, timeProvider.GetUtcNow() + RejectionLifetime));

    /// <summary>The login app's reason when the request came back from a rejected challenge; <see langword="null"/> otherwise.</summary>
    public static string? RejectionIn(IEnumerable<KeyValuePair<string, StringValues>> parameters, IDataProtectionProvider dataProtection)
    {
        var marker = parameters.FirstOrDefault(parameter => parameter.Key == RejectedParameter).Value.ToString();
        if (string.IsNullOrEmpty(marker))
        {
            return null;
        }

        try
        {
            return ProtectorFor(dataProtection).Unprotect(marker) is { Length: > 0 } reason ? reason : "The sign-in was refused.";
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static ITimeLimitedDataProtector ProtectorFor(IDataProtectionProvider dataProtection) =>
        dataProtection.CreateProtector("Kimlik.Interactions.Rejection").ToTimeLimitedDataProtector();
}
