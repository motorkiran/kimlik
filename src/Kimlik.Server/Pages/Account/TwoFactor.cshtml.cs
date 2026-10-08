using Kimlik.Application.Accounts;
using Kimlik.Application.Mfa;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// Sets up, inspects and turns off two-factor authentication. New recovery codes and turning it off take a current
/// code from the authenticator app.
/// </summary>
public sealed class TwoFactorModel(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    TwoFactor twoFactor,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    /// <summary>The code from the authenticator app, for whichever form was sent.</summary>
    [BindProperty]
    public string? Code { get; set; }

    public MfaStatusResponse? Status { get; private set; }

    /// <summary>Set while setting up: the key to add to the app.</summary>
    public AuthenticatorKeyView? Key { get; private set; }

    /// <summary>Set once new recovery codes exist: the codes to save.</summary>
    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    public string? Notice { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => ShowStatusAsync(cancellationToken);

    public async Task<IActionResult> OnPostSetUpAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user || user.TwoFactorEnabled)
        {
            return await ShowStatusAsync(cancellationToken);
        }

        // Creating the key updates the security stamp.
        await ShowKeyAsync(user);
        await KeepThisSessionAsync(signInManager);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        if (await userManager.GetUserAsync(User) is not { } user || user.TwoFactorEnabled)
        {
            return await ShowStatusAsync(cancellationToken);
        }

        if (CheckCode() is { } refused)
        {
            ErrorMessage = refused;
            await ShowKeyAsync(user);
            return Page();
        }

        var confirmed = await twoFactor.ConfirmSetupAsync(user, Code!, cancellationToken);
        if (confirmed.IsFailure)
        {
            ErrorMessage = MessageFor(confirmed.Error);
            await ShowKeyAsync(user);
            return Page();
        }

        await KeepThisSessionAsync(signInManager);
        RecoveryCodes = confirmed.Value.Codes;
        return Page();
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync(CancellationToken cancellationToken)
    {
        if (CheckCode() is { } refused)
        {
            ErrorMessage = refused;
            return await ShowStatusAsync(cancellationToken);
        }

        var regenerated = await twoFactor.RegenerateRecoveryCodesAsync(UserId, Code!, cancellationToken);
        if (regenerated.IsFailure)
        {
            ErrorMessage = MessageFor(regenerated.Error);
            return await ShowStatusAsync(cancellationToken);
        }

        RecoveryCodes = regenerated.Value.Codes;
        return Page();
    }

    public async Task<IActionResult> OnPostTurnOffAsync(CancellationToken cancellationToken)
    {
        if (CheckCode() is { } refused)
        {
            ErrorMessage = refused;
            return await ShowStatusAsync(cancellationToken);
        }

        var disabled = await twoFactor.DisableAsync(UserId, Code!, cancellationToken);
        if (disabled.IsFailure)
        {
            ErrorMessage = MessageFor(disabled.Error);
            return await ShowStatusAsync(cancellationToken);
        }

        await KeepThisSessionAsync(signInManager);
        Notice = localizer["Two-factor authentication is off."];
        return await ShowStatusAsync(cancellationToken);
    }

    /// <summary>Why the code cannot be checked, if it cannot.</summary>
    private string? CheckCode()
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            return localizer["Enter the code."];
        }

        if (!throttle.TryAcquire(ThrottledAction.SignIn, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return localizer["Too many attempts. Wait a minute and try again."];
        }

        return null;
    }

    private string MessageFor(Error error) => error switch
    {
        _ when error == MfaErrors.InvalidCode => localizer["That code is not right. Check it and try again."],
        _ when error == MfaErrors.Required => localizer["Your account requires two-factor authentication, so it cannot be turned off."],
        _ when error == AccountErrors.LockedOut => localizer["Too many failed attempts. Try again later."],
        _ => localizer["Something went wrong. Try again."],
    };

    private async Task<IActionResult> ShowStatusAsync(CancellationToken cancellationToken)
    {
        var status = await twoFactor.StatusAsync(UserId, cancellationToken);
        if (status.IsFailure)
        {
            return Challenge();
        }

        Status = status.Value;
        return Page();
    }

    private async Task ShowKeyAsync(User user) => Key = AuthenticatorKeyView.For((await twoFactor.PendingSetupAsync(user)).Value);
}
