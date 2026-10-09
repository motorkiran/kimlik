using System.Security.Claims;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Mfa;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Kimlik.Server.Oidc;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Pages.Connect;

/// <summary>
/// Where people approve a device that asked to sign in (RFC 8628): signed in here, they enter the code the device
/// shows, unless its link carried it, and allow or deny it. It always asks, first-party apps included, since the request
/// comes from another device; and an account that needs a second factor adds it to the session first. An administrator
/// acting as the user cannot approve devices, whose tokens would outlast the impersonation.
/// </summary>
[Authorize]
[NotWhileImpersonating]
public sealed class VerifyModel(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    ScopeDescriptions scopeDescriptions,
    OidcPrincipalFactory principalFactory,
    MfaPolicy mfaPolicy,
    UserManager<User> userManager,
    IKimlikDbContext context,
    IAuditLog auditLog) : PageModel
{
    public const string Approved = "approved";
    public const string Denied = "denied";

    public string? UserCode { get; private set; }

    public string? ApplicationName { get; private set; }

    public IReadOnlyList<string> ScopeDescriptions { get; private set; } = [];

    /// <summary>How the request ended: <see cref="Approved"/> or <see cref="Denied"/>.</summary>
    public string? Outcome { get; private set; }

    public bool InvalidCode { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? outcome, CancellationToken cancellationToken)
    {
        if (outcome is Approved or Denied)
        {
            Outcome = outcome;
            return Page();
        }

        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The verification request cannot be retrieved.");
        UserCode = request.UserCode;
        if (string.IsNullOrEmpty(UserCode))
        {
            return Page();
        }

        if (await DeviceRequestAsync() is not { } device)
        {
            InvalidCode = true;
            return Page();
        }

        if (await StepUpAsync(cancellationToken) is { } stepUp)
        {
            return stepUp;
        }

        var application = await applications.FindByClientIdAsync(device.GetClaim(Claims.ClientId)!, cancellationToken);
        ApplicationName = application is null ? device.GetClaim(Claims.ClientId) : await applications.GetLocalizedDisplayNameAsync(application, cancellationToken);
        ScopeDescriptions = await scopeDescriptions.DescribeAsync(device.GetScopes(), cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? decision, CancellationToken cancellationToken)
    {
        UserCode = HttpContext.GetOpenIddictServerRequest()?.UserCode;
        if (await DeviceRequestAsync() is not { } device || await userManager.GetUserAsync(User) is not { } user)
        {
            InvalidCode = true;
            return Page();
        }

        if (await StepUpAsync(cancellationToken) is { } stepUp)
        {
            return stepUp;
        }

        if (decision != "allow")
        {
            return Forbid(new AuthenticationProperties { RedirectUri = $"{Request.PathBase}/connect/verify?outcome={Denied}" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var identity = await principalFactory.CreateAsync(
            user,
            device.GetScopes(),
            resources: null,
            SignInFlow.SignedInAt(session),
            OidcPrincipalFactory.AuthenticationMethodsOf(session.Principal!),
            organizationId: null,
            cancellationToken);

        // The device's own authorization ties its tokens together and lists it among the user's sessions.
        var clientId = device.GetClaim(Claims.ClientId)!;
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");
        var authorization = await authorizations.CreateAsync(
            identity, user.Id.ToString(), (await applications.GetIdAsync(application, cancellationToken))!, AuthorizationTypes.AdHoc, identity.GetScopes(), cancellationToken);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));

        auditLog.Record(AuditActions.UserDeviceApproved, AuditSubject.User(user.Id), new Dictionary<string, object?> { ["client"] = clientId });
        await context.SaveChangesAsync(cancellationToken);

        return SignIn(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { RedirectUri = $"{Request.PathBase}/connect/verify?outcome={Approved}" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>The device's request behind the code in this request, if the code is valid.</summary>
    private async Task<ClaimsPrincipal?> DeviceRequestAsync()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return result is { Succeeded: true, Principal: { } principal } && principal.GetClaim(Claims.ClientId) is { Length: > 0 } ? principal : null;
    }

    /// <summary>Sends a session without the second factor its account needs to add it, and back here.</summary>
    private async Task<IActionResult?> StepUpAsync(CancellationToken cancellationToken)
    {
        if (User.HasClaim(SignInFlow.MethodClaim, SignInFlow.MultiFactorMethod)
            || !await mfaPolicy.IsRequiredAsync(Guid.Parse(userManager.GetUserId(User)!), cancellationToken))
        {
            return null;
        }

        var retry = $"{Request.PathBase}{Request.Path}?user_code={Uri.EscapeDataString(UserCode ?? string.Empty)}";
        return LocalRedirect($"{Request.PathBase}/signin/step-up?returnUrl={Uri.EscapeDataString(retry)}");
    }
}
