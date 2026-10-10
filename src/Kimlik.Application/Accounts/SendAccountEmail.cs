using Kimlik.Application.Abstractions;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

public enum AccountEmail
{
    EmailVerification,
    PasswordReset,

    /// <summary>Someone tried to sign up with an address that already has an account.</summary>
    AlreadyRegistered,

    /// <summary>A one-time code to sign in with.</summary>
    SignInCode,
}

/// <summary>
/// Sends an account email. Only the intent is stored in the outbox; the one-time token is generated at send
/// time, so no usable secret ever sits in the database.
/// </summary>
[OutboxMessage("account.email")]
public sealed record SendAccountEmail(Guid UserId, AccountEmail Kind, string? ReturnUrl = null);

public sealed class SendAccountEmailHandler(
    UserManager<User> userManager,
    IAccountLinks links,
    IEmailTemplateRenderer renderer,
    IEmailSender sender) : IOutboxMessageHandler<SendAccountEmail>
{
    public async Task HandleAsync(SendAccountEmail message, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(message.UserId.ToString());
        if (user?.Email is null)
        {
            // The account was deleted while the message was waiting.
            return;
        }

        var (template, link) = message.Kind switch
        {
            AccountEmail.EmailVerification when user.EmailConfirmed => (null, null),
            AccountEmail.EmailVerification => ("email-verification",
                links.EmailVerification(user.Id, await userManager.GenerateEmailConfirmationTokenAsync(user), message.ReturnUrl)),
            AccountEmail.PasswordReset => ("password-reset",
                links.PasswordReset(user.Id, await userManager.GeneratePasswordResetTokenAsync(user))),
            AccountEmail.AlreadyRegistered => ("already-registered", links.SignIn()),
            AccountEmail.SignInCode => ("sign-in-code", null),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Kind, "Unknown account email."),
        };

        if (template is null)
        {
            return;
        }

        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = user.GivenName ?? user.Name,
            ["email"] = user.Email,
        };

        if (message.Kind == AccountEmail.SignInCode)
        {
            // Created now, as the email goes out, and replacing any earlier code.
            var code = await userManager.GenerateUserTokenAsync(user, OneTimeCodes.TokenProvider, OneTimeCodes.EmailSignIn);
            model["code"] = code;
            link = links.SignInWithCode(code);
        }

        model["link"] = link!.AbsoluteUri;

        var email = await renderer.RenderAsync(template, user.Locale, model, cancellationToken);
        await sender.SendAsync(new EmailMessage(user.Email, email.Subject, email.HtmlBody, email.TextBody), cancellationToken);
    }
}
