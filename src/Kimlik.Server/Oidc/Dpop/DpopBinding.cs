using System.Security.Claims;
using Kimlik.Security;
using Microsoft.Net.Http.Headers;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc.Dpop;

/// <summary>How access tokens bound to a key are used at Kimlik's API and UserInfo (RFC 9449, section 7).</summary>
internal static class DpopBinding
{
    /// <summary>The OpenIddict transaction property with the thumbprint of the key the tokens being issued are bound to.</summary>
    public const string ThumbprintProperty = "kimlik:dpop_jkt";

    /// <summary>
    /// The claim of authorization codes and refresh tokens with the thumbprint of the key they are bound to. It has no
    /// destination, so it stays out of access and ID tokens.
    /// </summary>
    public const string ThumbprintClaim = "kimlik_dpop_jkt";

    /// <summary>The authorization request parameter that binds the code to a key (RFC 9449, section 10).</summary>
    public const string ThumbprintParameter = "dpop_jkt";

    public const string InvalidThumbprint = $"{ThumbprintParameter} must be the JWK SHA-256 thumbprint of the key, base64url-encoded.";

    private const string SchemePrefix = DpopProof.Scheme + " ";

    /// <summary>The access token of a request that sends it as <c>Authorization: DPoP</c>.</summary>
    public static string? TokenOf(HttpRequest request) =>
        request.Headers.Authorization is [{ } header] && header.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase) && header.Length > SchemePrefix.Length
            ? header[SchemePrefix.Length..].Trim()
            : null;

    /// <summary>Whether <paramref name="value"/> can be a JWK SHA-256 thumbprint: 32 bytes, base64url-encoded.</summary>
    public static bool IsThumbprint(string? value) =>
        value is { Length: 43 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>
    /// Refuses an access token that is bound to a key but comes without a proof by it, or as a bearer token, and an
    /// unbound one that comes as a DPoP token. Returns the error and its description, or <see langword="null"/>.
    /// </summary>
    public static async Task<(string Error, string Description)?> CheckAsync(
        DpopProofValidator validator, HttpRequest request, string token, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var boundTo = DpopProof.BoundKeyOf(principal.GetClaim(Claims.Confirmation));
        var asDpop = TokenOf(request) is not null;

        if (boundTo is null)
        {
            return asDpop ? (Errors.InvalidToken, "The access token is not bound to a key. Send it as a bearer token.") : null;
        }

        if (!asDpop)
        {
            return (Errors.InvalidToken, "The access token is bound to a key. Send it as a DPoP token, with a proof.");
        }

        var (proof, error) = await validator.ValidateAsync(request, token, cancellationToken);
        if (proof is null)
        {
            return (DpopProof.InvalidProofError, error ?? "Send a DPoP proof with the access token.");
        }

        return string.Equals(proof.Thumbprint, boundTo, StringComparison.Ordinal)
            ? null
            : (DpopProof.InvalidProofError, "The DPoP proof is not signed by the key the access token is bound to.");
    }

    /// <summary>
    /// Turns the bearer challenge OpenIddict wrote into a DPoP one, with the algorithms Kimlik takes, when the request sent
    /// a DPoP token or the proof was refused (RFC 9449, section 7.1).
    /// </summary>
    public static void Challenge(HttpRequest request, string? error)
    {
        if (string.IsNullOrEmpty(error) || (TokenOf(request) is null && error != DpopProof.InvalidProofError))
        {
            return;
        }

        var response = request.HttpContext.Response;
        var bearer = response.Headers.WWWAuthenticate.ToString();
        var parameters = bearer.StartsWith(Schemes.Bearer, StringComparison.OrdinalIgnoreCase) ? bearer[Schemes.Bearer.Length..] : "";
        response.Headers[HeaderNames.WWWAuthenticate] =
            $"{DpopProof.Scheme} algs=\"{string.Join(' ', DpopProof.Algorithms)}\"" + (parameters.Length > 0 ? "," + parameters : "");

        if (error == DpopProof.InvalidProofError)
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
        }
    }
}
