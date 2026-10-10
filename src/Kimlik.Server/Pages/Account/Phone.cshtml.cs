using System.ComponentModel.DataAnnotations;
using Kimlik.Application.Accounts;
using Kimlik.Domain.Users;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages.Account;

/// <summary>
/// The user's phone number, for signing in with texted codes: added or changed by entering the code texted to it, and
/// removed. Adding one takes a recent sign-in, so that someone holding a stolen session cannot add a number of their own.
/// </summary>
public sealed class PhoneModel(
    UserManager<User> userManager,
    UserPhoneNumbers phoneNumbers,
    IOptions<SmsOptions> sms,
    AccountErrorMessages errorMessages,
    TimeProvider timeProvider,
    IStringLocalizer<SharedResource> localizer) : AccountPageModel
{
    /// <summary>The account's verified number, if it has one.</summary>
    public string? PhoneNumber { get; private set; }

    public bool SignedInRecently { get; private set; }

    /// <summary>The number a code was texted to, waiting for the code.</summary>
    [BindProperty]
    public string? PendingNumber { get; set; }

    [BindProperty]
    public PhoneInput Input { get; set; } = new();

    public string? Message { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool saved, bool removed)
    {
        if (!sms.Value.Enabled)
        {
            return NotFound();
        }

        await LoadAsync();
        Message = saved ? localizer["Your phone number has been saved."].Value : removed ? localizer["Your phone number has been removed."].Value : null;
        return Page();
    }

    /// <summary>Texts a code to the number the user entered.</summary>
    public async Task<IActionResult> OnPostSendAsync(CancellationToken cancellationToken)
    {
        ModelState.Remove("Input.Code");
        await LoadAsync();
        if (!SignedInRecently || !ModelState.IsValid)
        {
            return Page();
        }

        var sent = await phoneNumbers.SendCodeAsync(UserId, Input.PhoneNumber, cancellationToken);
        if (sent.IsFailure)
        {
            ModelState.AddModelError("Input.PhoneNumber", errorMessages.For(sent.Error));
            return Page();
        }

        PendingNumber = sent.Value;
        Message = localizer["We texted a code to {0}. Enter it below.", sent.Value];
        return Page();
    }

    /// <summary>Saves the number once the code texted to it is right.</summary>
    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        ModelState.Remove("Input.PhoneNumber");
        await LoadAsync();
        if (!SignedInRecently || PendingNumber is null || !ModelState.IsValid)
        {
            return Page();
        }

        var confirmed = await phoneNumbers.ConfirmAsync(UserId, PendingNumber, Input.Code ?? string.Empty, cancellationToken);
        if (confirmed.IsFailure)
        {
            ErrorMessage = errorMessages.For(confirmed.Error);
            return Page();
        }

        return RedirectToPage(new { saved = "true" });
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        await phoneNumbers.RemoveAsync(UserId, cancellationToken);
        return RedirectToPage(new { removed = "true" });
    }

    private async Task LoadAsync()
    {
        PhoneNumber = (await userManager.GetUserAsync(User))?.PhoneNumber;
        SignedInRecently = SignInFlow.SignedInRecently(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme), timeProvider.GetUtcNow());
    }
}

public sealed class PhoneInput
{
    [Required(ErrorMessage = "Enter your phone number.")]
    [StringLength(32, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Enter the code.")]
    [StringLength(16, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Code")]
    public string? Code { get; set; }
}
