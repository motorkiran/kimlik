using Kimlik.Domain.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Server.Identity;

/// <summary>
/// The server side of the passkey ceremonies, through Identity's passkey handler. The browser asks for options, has
/// the authenticator answer them and posts the answer back. In between, the ceremony's state (its challenge) travels
/// with the page as a protected value that expires after five minutes, and for a new passkey only works for the user
/// it was made for; nothing is kept on the server or in cookies.
/// </summary>
public sealed class PasskeyCeremonies(IPasskeyHandler<User> handler, IDataProtectionProvider dataProtection)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The options for the browser's WebAuthn API, as JSON, and the protected state to post back with the answer.</summary>
    public sealed record Challenge(string OptionsJson, string State);

    /// <summary>Starts adding a passkey to <paramref name="user"/>; the authenticator excludes the ones it already holds.</summary>
    public async Task<Challenge> BeginCreationAsync(User user, HttpContext context)
    {
        var entity = new PasskeyUserEntity { Id = user.Id.ToString(), Name = user.Email!, DisplayName = user.Name ?? user.Email! };
        var options = await handler.MakeCreationOptionsAsync(entity, context);
        return new Challenge(options.CreationOptionsJson, CreationProtector(user).Protect(options.AttestationState ?? string.Empty, StateLifetime));
    }

    /// <summary>The new passkey, if the authenticator's answer is valid for the state that went out to this user.</summary>
    public async Task<UserPasskeyInfo?> CompleteCreationAsync(User user, string credentialJson, string state, HttpContext context)
    {
        if (Unprotect(CreationProtector(user), state) is not { } attestationState)
        {
            return null;
        }

        var result = await handler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            CredentialJson = credentialJson,
            AttestationState = attestationState,
            HttpContext = context,
        });

        return result.Succeeded && result.UserEntity.Id == user.Id.ToString() ? result.Passkey : null;
    }

    /// <summary>Starts a sign-in with any passkey the browser holds for Kimlik.</summary>
    public async Task<Challenge> BeginAssertionAsync(HttpContext context)
    {
        var options = await handler.MakeRequestOptionsAsync(null, context);
        return new Challenge(options.RequestOptionsJson, AssertionProtector().Protect(options.AssertionState ?? string.Empty, StateLifetime));
    }

    /// <summary>The user and their passkey, with its updated counter, if the authenticator's answer is valid.</summary>
    public async Task<PasskeyAssertionResult<User>?> CompleteAssertionAsync(string credentialJson, string state, HttpContext context)
    {
        if (Unprotect(AssertionProtector(), state) is not { } assertionState)
        {
            return null;
        }

        var result = await handler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            CredentialJson = credentialJson,
            AssertionState = assertionState,
            HttpContext = context,
        });

        return result.Succeeded ? result : null;
    }

    private ITimeLimitedDataProtector CreationProtector(User user) =>
        dataProtection.CreateProtector("Kimlik.Passkeys.Creation", user.Id.ToString()).ToTimeLimitedDataProtector();

    private ITimeLimitedDataProtector AssertionProtector() =>
        dataProtection.CreateProtector("Kimlik.Passkeys.Assertion").ToTimeLimitedDataProtector();

    private static string? Unprotect(ITimeLimitedDataProtector protector, string state)
    {
        try
        {
            return protector.Unprotect(state);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, made for another user, or tampered with.
            return null;
        }
    }
}
