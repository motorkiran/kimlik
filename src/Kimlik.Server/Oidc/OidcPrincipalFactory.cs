using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kimlik.Application.Access;
using Kimlik.Application.Plans;
using Kimlik.Contracts;
using Kimlik.Domain.Plans;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

/// <summary>
/// Builds the identity behind a user's tokens from the current state of the account, so every code
/// exchange and refresh reflects profile changes.
/// </summary>
public sealed class OidcPrincipalFactory(IOpenIddictScopeManager scopes, AccessResolver access, Entitlements entitlements)
{
    /// <summary>The claim naming who acts for the subject (RFC 8693): an administrator impersonating the user.</summary>
    public const string ActorClaim = "act";

    /// <summary>
    /// Creates the identity for tokens issued to <paramref name="user"/>, acting in <paramref name="organizationId"/>
    /// if given; the caller has checked the membership. The audiences are <paramref name="resources"/> when given (a
    /// refresh keeps the original ones), otherwise those of the scopes.
    /// </summary>
    public async Task<ClaimsIdentity> CreateAsync(
        User user,
        ImmutableArray<string> grantedScopes,
        ImmutableArray<string>? resources,
        DateTimeOffset? authenticatedAt,
        IReadOnlyList<string> authenticationMethods,
        Guid? organizationId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.Name)
            .SetClaim(Claims.GivenName, user.GivenName)
            .SetClaim(Claims.FamilyName, user.FamilyName)
            .SetClaim(Claims.Locale, user.Locale)
            .SetClaim(Claims.Picture, user.PictureUrl)
            .SetClaim(Claims.Zoneinfo, user.TimeZone);

        identity.AddClaim(new Claim(Claims.EmailVerified, user.EmailConfirmed ? "true" : "false", ClaimValueTypes.Boolean));

        // Kimlik keeps verified numbers only.
        if (user.PhoneNumber is not null)
        {
            identity.SetClaim(Claims.PhoneNumber, user.PhoneNumber);
            identity.AddClaim(new Claim(Claims.PhoneNumberVerified, user.PhoneNumberConfirmed ? "true" : "false", ClaimValueTypes.Boolean));
        }
        identity.AddClaim(UnixTimeClaim(Claims.UpdatedAt, user.UpdatedAt));

        if (authenticatedAt is not null)
        {
            identity.AddClaim(UnixTimeClaim(Claims.AuthenticationTime, authenticatedAt.Value));
        }

        AddArrayClaim(identity, Claims.AuthenticationMethodReference, authenticationMethods);
        identity.SetClaim(Claims.AuthenticationContextReference, AuthenticationContexts.Of(authenticationMethods));

        // The browser session the tokens came from, which logout tokens name (OpenID Connect Back-Channel Logout).
        identity.SetClaim(JwtRegisteredClaimNames.Sid, sessionId);

        identity.SetScopes(grantedScopes);
        identity.SetResources(resources ?? [.. await scopes.ListResourcesAsync(grantedScopes, cancellationToken).ToListAsync(cancellationToken)]);
        AddAccess(identity, await access.ForUserAsync(user.Id, organizationId, cancellationToken));

        var subscriber = organizationId is { } organization ? Subscriber.Organization(organization) : Subscriber.User(user.Id);
        var entitlementSource = await entitlements.SourceOfAsync(subscriber, cancellationToken);
        identity.SetClaim(KimlikClaimTypes.Plan, entitlementSource.Plan?.Key);
        if (entitlementSource.IsCustom)
        {
            identity.AddClaim(new Claim(KimlikClaimTypes.CustomEntitlements, "true", ClaimValueTypes.Boolean));
        }

        identity.SetDestinations(GetDestinations);

        return identity;
    }

    /// <summary>
    /// Adds the subject's roles and permissions for the audiences already set on the identity. Each is one
    /// claim typed as a JSON array, so a single role is still written as an array: OpenIddict's usual
    /// one-claim-per-value form would turn a lone value into a plain string, and resource servers would
    /// have to handle two shapes.
    /// </summary>
    public static void AddAccess(ClaimsIdentity identity, AccessGrant grant)
    {
        grant = grant.ForAudiences(identity.GetResources());

        AddArrayClaim(identity, KimlikClaimTypes.Roles, grant.Roles);
        AddArrayClaim(identity, KimlikClaimTypes.Permissions, grant.Permissions);

        identity.RemoveClaims(KimlikClaimTypes.OrganizationId);
        identity.RemoveClaims(KimlikClaimTypes.OrganizationRoles);
        if (grant.Organization is { } organization)
        {
            identity.SetClaim(KimlikClaimTypes.OrganizationId, organization.OrganizationId.ToString());
            AddArrayClaim(identity, KimlikClaimTypes.OrganizationRoles, organization.Roles);
        }
    }

    /// <summary>
    /// Marks the tokens as issued to the administrator <paramref name="actorId"/> acting as the user: the actor claim
    /// names them, and the tokens expire within <paramref name="lifetime"/>, with the impersonation.
    /// </summary>
    public static void AddActor(ClaimsIdentity identity, string actorId, TimeSpan lifetime)
    {
        identity.RemoveClaims(ActorClaim);
        identity.AddClaim(ActorClaim, new Dictionary<string, string?> { [Claims.Subject] = actorId });
        identity.SetAccessTokenLifetime(lifetime).SetIdentityTokenLifetime(lifetime);
        identity.SetDestinations(GetDestinations);
    }

    /// <summary>
    /// Marks a token that a client got by exchange (RFC 8693) as the client acting for the user: <c>act</c> names the
    /// client, with the subject token's own <c>act</c>, such as an impersonating administrator, nested inside.
    /// </summary>
    public static void AddDelegation(ClaimsIdentity identity, string clientId, ClaimsPrincipal subjectToken)
    {
        var actor = new JsonObject { [Claims.Subject] = clientId, [Claims.ClientId] = clientId };
        if (ActorOf(subjectToken) is { } previous)
        {
            actor[ActorClaim] = JsonNode.Parse(previous.GetRawText());
        }

        identity.RemoveClaims(ActorClaim);
        identity.AddClaim(ActorClaim, JsonSerializer.SerializeToElement(actor));
        identity.SetDestinations(GetDestinations);
    }

    /// <summary>
    /// The administrator acting as the user, in a token issued while they impersonate them, also once a client exchanged
    /// it: the first actor in the chain that is not a client.
    /// </summary>
    public static string? GetActor(ClaimsPrincipal principal)
    {
        for (var actor = ActorOf(principal); actor is { } current; actor = Nested(current))
        {
            if (!current.TryGetProperty(Claims.ClientId, out _) && current.TryGetProperty(Claims.Subject, out var subject) && subject.ValueKind == JsonValueKind.String)
            {
                return subject.GetString();
            }
        }

        return null;

        static JsonElement? Nested(JsonElement actor) =>
            actor.TryGetProperty(ActorClaim, out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : null;
    }

    /// <summary>Whether someone acts for the user in the token: an impersonating administrator, or a client that exchanged it.</summary>
    public static bool HasActor(ClaimsPrincipal principal) => ActorOf(principal) is not null;

    /// <summary>The token's <c>act</c> claim, a JSON object.</summary>
    private static JsonElement? ActorOf(ClaimsPrincipal principal)
    {
        if (principal.FindFirst(ActorClaim)?.Value is not { Length: > 0 } actor)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(actor);
            return json.RootElement.ValueKind == JsonValueKind.Object ? json.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The organization a previously issued token acts in, if any.</summary>
    public static Guid? GetOrganizationId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.GetClaim(KimlikClaimTypes.OrganizationId), out var organizationId) ? organizationId : null;

    private static void AddArrayClaim(ClaimsIdentity identity, string type, IReadOnlyList<string> values)
    {
        identity.RemoveClaims(type);

        if (values.Count > 0)
        {
            identity.AddClaim(new Claim(type, JsonSerializer.Serialize(values), JsonClaimValueTypes.JsonArray));
        }
    }

    /// <summary>
    /// How the user signed in, as method references (RFC 8176): a password (<c>pwd</c>), a code sent by email
    /// (<c>email</c>) or an account at another provider (<c>fed</c>, as Microsoft Entra ID uses it), and the second
    /// factor when one was used: a one-time code (<c>otp</c>, also when the browser was trusted after one), a passkey
    /// (<c>pop</c>) or the provider's own (just <c>fed</c> and <c>mfa</c>); or a passkey alone, which counts as two factors.
    /// </summary>
    public static IReadOnlyList<string> AuthenticationMethodsOf(ClaimsPrincipal session)
    {
        var firstFactor = SignInFlow.FirstFactorOf(session);
        if (firstFactor == SignInFlow.PasskeyMethod)
        {
            return [SignInFlow.PasskeyMethod, SignInFlow.MultiFactorMethod];
        }

        return SignInFlow.SecondFactorOf(session) is { } secondFactor ? [.. new[] { firstFactor, secondFactor, SignInFlow.MultiFactorMethod }.Distinct()] : [firstFactor];
    }

    /// <summary>The authentication methods a previously issued token carries, one claim per value or one JSON array.</summary>
    public static IReadOnlyList<string> GetAuthenticationMethods(ClaimsPrincipal principal) =>
    [
        .. principal.FindAll(Claims.AuthenticationMethodReference).SelectMany(claim => claim.ValueType == JsonClaimValueTypes.JsonArray
            ? JsonSerializer.Deserialize<string[]>(claim.Value) ?? []
            : [claim.Value]),
    ];

    /// <summary>When the user authenticated, as carried by a previously issued token.</summary>
    public static DateTimeOffset? GetAuthenticationTime(ClaimsPrincipal principal) =>
        long.TryParse(principal.GetClaim(Claims.AuthenticationTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>Profile, email and phone claims only leave Kimlik when the client was granted the matching scope.</summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        var identity = claim.Subject!;

        return claim.Type switch
        {
            Claims.Subject or Claims.AuthenticationTime or Claims.AuthenticationMethodReference or Claims.AuthenticationContextReference or ActorClaim
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Name or Claims.GivenName or Claims.FamilyName or Claims.Locale or Claims.Picture or Claims.Zoneinfo or Claims.UpdatedAt
                when identity.HasScope(Scopes.Profile)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Email or Claims.EmailVerified when identity.HasScope(Scopes.Email)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.PhoneNumber or Claims.PhoneNumberVerified when identity.HasScope(Scopes.Phone)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            KimlikClaimTypes.Roles or KimlikClaimTypes.Permissions or KimlikClaimTypes.OrganizationRoles or KimlikClaimTypes.Plan or KimlikClaimTypes.CustomEntitlements
                => [Destinations.AccessToken],

            KimlikClaimTypes.OrganizationId => [Destinations.AccessToken, Destinations.IdentityToken],

            JwtRegisteredClaimNames.Sid => [Destinations.IdentityToken],

            _ => [],
        };
    }

    private static Claim UnixTimeClaim(string type, DateTimeOffset value) =>
        new(type, value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
}
