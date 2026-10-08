namespace Kimlik.Application.Abstractions;

/// <summary>Absolute links to the hosted pages, for use in emails.</summary>
public interface IAccountLinks
{
    Uri SignIn();

    Uri EmailVerification(Guid userId, string token, string? returnUrl);

    Uri PasswordReset(Guid userId, string token);

    Uri Invitation(string token);
}
