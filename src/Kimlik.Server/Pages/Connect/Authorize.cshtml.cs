using System.Collections.Immutable;
using Kimlik.Domain.Users;
using Kimlik.Server.Oidc;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Pages.Connect;

/// <summary>
/// The authorization endpoint. OpenIddict validates the request first; this page then makes sure the user is
/// signed in, asks for consent when the client requires it and issues the authorization code.
/// </summary>
// Clients may send authorization requests as form posts, so the token is checked only for consent decisions.
[IgnoreAntiforgeryToken]
public sealed class AuthorizeModel(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictScopeManager scopes,
    UserManager<User> userManager,
    OidcPrincipalFactory principalFactory,
    IAntiforgery antiforgery,
    TimeProvider timeProvider,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    private const string ConsentField = "consent";
    private const string ConsentAccepted = "accept";

    public string? Error { get; private set; }

    public string? ErrorDescription { get; private set; }

    public string? ApplicationName { get; private set; }

    public IReadOnlyList<string> ScopeDescriptions { get; private set; } = [];

    /// <summary>The authorization request parameters, posted back together with the consent decision.</summary>
    public IEnumerable<KeyValuePair<string, StringValues>> RequestParameters =>
        (Request.HasFormContentType ? (IEnumerable<KeyValuePair<string, StringValues>>)Request.Form : Request.Query)
            .Where(parameter => parameter.Key is not ConsentField && parameter.Key != "__RequestVerificationToken");

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => AuthorizeAsync(cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) => AuthorizeAsync(cancellationToken);

    private async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken)
    {
        // Requests OpenIddict rejected without being able to redirect back to the client (unknown client,
        // unregistered redirect URI) end here and are shown to the user.
        if (HttpContext.GetOpenIddictServerResponse() is { Error: { Length: > 0 } error } response)
        {
            Error = error;
            ErrorDescription = response.ErrorDescription;
            return Page();
        }

        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = session.Succeeded ? await userManager.GetUserAsync(session.Principal) : null;

        if (user is { CanSignIn: false })
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            user = null;
        }

        if (user is null || request.HasPromptValue(PromptValues.Login) || IsOlderThanMaxAge(request, session.Properties))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ForbidWith(Errors.LoginRequired, "The user is not signed in.");
            }

            return Challenge(new AuthenticationProperties { RedirectUri = BuildRetryUri(request) }, IdentityConstants.ApplicationScheme);
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");
        var applicationId = await applications.GetIdAsync(application, cancellationToken);

        var existingAuthorizations = await authorizations.FindAsync(
            subject: user.Id.ToString(),
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes(),
            cancellationToken).ToListAsync(cancellationToken);

        if (Request.HasFormContentType && Request.Form.TryGetValue(ConsentField, out var decision))
        {
            await antiforgery.ValidateRequestAsync(HttpContext);

            return decision == ConsentAccepted
                ? await IssueCodeAsync(user, applicationId!, request, session.Properties, existingAuthorizations, cancellationToken)
                : ForbidWith(Errors.AccessDenied, "The user denied the request.");
        }

        switch (await applications.GetConsentTypeAsync(application, cancellationToken))
        {
            case ConsentTypes.External when existingAuthorizations.Count == 0:
                return ForbidWith(Errors.ConsentRequired, "The user is not allowed to access this application.");

            case ConsentTypes.Implicit:
            case ConsentTypes.External:
            case ConsentTypes.Explicit when existingAuthorizations.Count > 0 && !request.HasPromptValue(PromptValues.Consent):
                return await IssueCodeAsync(user, applicationId!, request, session.Properties, existingAuthorizations, cancellationToken);

            case ConsentTypes.Explicit or ConsentTypes.Systematic when request.HasPromptValue(PromptValues.None):
                return ForbidWith(Errors.ConsentRequired, "Interactive user consent is required.");

            default:
                ApplicationName = await applications.GetLocalizedDisplayNameAsync(application, cancellationToken) ?? request.ClientId;
                ScopeDescriptions = await DescribeScopesAsync(request.GetScopes(), cancellationToken);
                return Page();
        }
    }

    private async Task<IActionResult> IssueCodeAsync(
        User user,
        string applicationId,
        OpenIddictRequest request,
        AuthenticationProperties? session,
        List<object> existingAuthorizations,
        CancellationToken cancellationToken)
    {
        var identity = await principalFactory.CreateAsync(user, request.GetScopes(), resources: null, session?.IssuedUtc, cancellationToken);

        // A permanent authorization records the consent and ties together every token issued under it.
        var authorization = existingAuthorizations.LastOrDefault()
            ?? await authorizations.CreateAsync(identity, user.Id.ToString(), applicationId, AuthorizationTypes.Permanent, identity.GetScopes(), cancellationToken);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));

        return SignIn(new System.Security.Claims.ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ForbidResult ForbidWith(string error, string description) =>
        Forbid(OidcResults.ErrorProperties(error, description), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private bool IsOlderThanMaxAge(OpenIddictRequest request, AuthenticationProperties? session) =>
        request.MaxAge is { } maxAge
        && session?.IssuedUtc is { } authenticatedAt
        && timeProvider.GetUtcNow() - authenticatedAt > TimeSpan.FromSeconds(maxAge);

    /// <summary>
    /// Where to return after signing in: the same authorization request without <c>prompt=login</c>,
    /// so the user is not asked to sign in again in a loop.
    /// </summary>
    private string BuildRetryUri(OpenIddictRequest request)
    {
        var parameters = RequestParameters.Where(parameter => parameter.Key != Parameters.Prompt).ToList();

        var prompt = string.Join(' ', request.GetPromptValues().Remove(PromptValues.Login));
        if (prompt.Length > 0)
        {
            parameters.Add(new(Parameters.Prompt, prompt));
        }

        return Request.PathBase + Request.Path + QueryString.Create(parameters);
    }

    private async Task<IReadOnlyList<string>> DescribeScopesAsync(ImmutableArray<string> requestedScopes, CancellationToken cancellationToken)
    {
        var descriptions = new List<string>();

        foreach (var scope in requestedScopes)
        {
            var description = scope switch
            {
                Scopes.OpenId => localizer["Confirm your identity"].Value,
                Scopes.Profile => localizer["See your name and profile"].Value,
                Scopes.Email => localizer["See your email address"].Value,
                Scopes.OfflineAccess => localizer["Stay signed in to it"].Value,
                _ => await scopes.FindByNameAsync(scope, cancellationToken) is { } apiScope
                    ? await scopes.GetLocalizedDisplayNameAsync(apiScope, cancellationToken) ?? scope
                    : scope,
            };

            descriptions.Add(description);
        }

        return descriptions;
    }
}
