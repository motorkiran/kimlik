namespace Kimlik.Application.Abstractions;

/// <summary>Absolute links to the hosted pages, for use in emails.</summary>
public interface IAccountLinks
{
    Uri SignIn();

    /// <summary>The page for a sign-in code, with <paramref name="code"/> filled in; it works in the browser that asked for it.</summary>
    Uri SignInWithCode(string code);

    Uri EmailVerification(Guid userId, string token, string? returnUrl);

    Uri PasswordReset(Guid userId, string token);

    Uri Invitation(string token);
}
