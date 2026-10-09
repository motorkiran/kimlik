using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Kimlik.Application.Accounts;
using Kimlik.Application.Organizations;
using Kimlik.Domain.Users;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Pages;

public sealed class SignUpModel(
    RegisterUserHandler registerUser,
    FindInvitationHandler findInvitation,
    EmailSignIn emailSignIn,
    PendingEmailCode pendingEmailCode,
    SignInFlow signInFlow,
    AccountErrorMessages errorMessages,
    RequestThrottle throttle,
    IStringLocalizer<SharedResource> localizer,
    IOptions<AccountOptions> accounts) : PageModel
{
    [BindProperty]
    public SignUpInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>The token of the invitation the person follows, if any.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Invitation { get; set; }

    /// <summary>The address of the open invitation; the account is created for it.</summary>
    public string? InvitedEmail { get; private set; }

    public bool CanSignUp => accounts.Value.Registration switch
    {
        RegistrationMode.Open => true,
        RegistrationMode.InviteOnly => InvitedEmail is not null,
        _ => false,
    };

    public int PasswordMinimumLength => accounts.Value.PasswordMinimumLength;

    /// <summary>Without a password, people sign in with codes sent to their address.</summary>
    public bool PasswordOptional => emailSignIn.Enabled;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadInvitationAsync(cancellationToken);
        Input.Email = InvitedEmail ?? Input.Email;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadInvitationAsync(cancellationToken);
        Input.Email = InvitedEmail ?? Input.Email;
        if (string.IsNullOrEmpty(Input.Password) && !PasswordOptional)
        {
            ModelState.AddModelError("Input.Password", localizer["Choose a password."]);
        }

        if (!CanSignUp || !ModelState.IsValid)
        {
            return Page();
        }

        if (!throttle.TryAcquire(ThrottledAction.SignUp, HttpContext))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError(string.Empty, localizer["Too many attempts. Wait a minute and try again."]);
            return Page();
        }

        var command = new RegisterUserCommand(
            Input.Email,
            string.IsNullOrEmpty(Input.Password) ? null : Input.Password,
            Input.GivenName,
            Input.FamilyName,
            CultureInfo.CurrentUICulture.Name,
            AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl : null,
            Invitation);
        var result = await registerUser.HandleAsync(command, cancellationToken);

        var passwordless = command.Password is null;
        if (result.IsSuccess)
        {
            if (!result.Value.EmailConfirmed && accounts.Value.RequireVerifiedEmail)
            {
                return passwordless ? AskForCode() : RedirectToPage("/SignUpComplete");
            }

            var returnUrl = AccountLinks.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
            var firstFactor = passwordless ? SignInFlow.EmailMethod : SignInFlow.PasswordMethod;
            return LocalRedirect(await signInFlow.ContinueAsync(result.Value, persistent: false, provider: null, returnUrl, cancellationToken, firstFactor));
        }

        // When addresses must be verified, a taken address gets the same answer as a new one,
        // so sign-up cannot be used to find out who has an account.
        if (result.Error == AccountErrors.EmailAlreadyRegistered && accounts.Value.RequireVerifiedEmail)
        {
            return passwordless ? AskForCode() : RedirectToPage("/SignUpComplete");
        }

        ModelState.AddModelError(errorMessages.FieldFor(result.Error), errorMessages.For(result.Error));
        return Page();
    }

    /// <summary>The code sent to the address signs the person in, and verifies the address of a new account.</summary>
    private RedirectToPageResult AskForCode()
    {
        pendingEmailCode.Start(HttpContext, Input.Email.Trim(), persistent: false, ReturnUrl);
        return RedirectToPage("/SignInCode", new { ReturnUrl });
    }

    private async Task LoadInvitationAsync(CancellationToken cancellationToken)
    {
        if (Invitation is { Length: > 0 } token && await findInvitation.HandleAsync(token, cancellationToken) is { IsSuccess: true } invitation)
        {
            InvitedEmail = invitation.Value.Email;
        }
    }
}

public sealed class SignUpInput
{
    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "First name")]
    public string? GivenName { get; set; }

    [StringLength(User.NameMaxLength, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Last name")]
    public string? FamilyName { get; set; }

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Use at most {1} characters.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [StringLength(AccountOptions.PasswordMaximumLength, ErrorMessage = "Use at most {1} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }
}
