using Kimlik.Application.Accounts;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Infrastructure.Identity;

/// <summary>
/// Rejects passwords longer than <see cref="AccountOptions.PasswordMaximumLength"/> characters, so an attacker
/// cannot make the server hash megabytes of input.
/// </summary>
internal sealed class MaximumLengthPasswordValidator<TUser> : IPasswordValidator<TUser>
    where TUser : class
{
    public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password) =>
        Task.FromResult(password?.Length > AccountOptions.PasswordMaximumLength
            ? IdentityResult.Failed(new IdentityError
            {
                Code = AccountErrors.PasswordTooLongCode,
                Description = $"Passwords must be at most {AccountOptions.PasswordMaximumLength} characters.",
            })
            : IdentityResult.Success);
}
