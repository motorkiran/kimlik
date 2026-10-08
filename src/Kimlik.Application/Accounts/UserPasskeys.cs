using System.Buffers.Text;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Users;
using Kimlik.Contracts.Account;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Kimlik.Application.Accounts;

/// <summary>
/// A user's passkeys, for the user and for administrators: listed, renamed and removed. A passkey is only created in
/// the browser, whose ceremony on the hosted pages hands the result to <see cref="AddAsync"/>.
/// </summary>
public sealed class UserPasskeys(UserManager<User> userManager, IKimlikDbContext context, IAuditLog auditLog)
{
    public const int MaxPerUser = 25;

    public const int NameMaxLength = 64;

    public async Task<Result<IReadOnlyList<PasskeyResponse>>> ListAsync(Guid userId)
    {
        if (await userManager.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return UserErrors.NotFound;
        }

        return (await userManager.GetPasskeysAsync(user)).OrderBy(passkey => passkey.CreatedAt).Select(ToResponse).ToList();
    }

    public async Task<Result<PasskeyResponse>> AddAsync(User user, UserPasskeyInfo passkey, string? name, CancellationToken cancellationToken)
    {
        if ((await userManager.GetPasskeysAsync(user)).Count >= MaxPerUser)
        {
            return AccountErrors.TooManyPasskeys;
        }

        passkey.Name = NameOf(name);
        var added = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!added.Succeeded)
        {
            return AccountErrors.FromIdentity(added.Errors);
        }

        await RecordAsync(AuditActions.UserPasskeyAdded, user.Id, passkey, cancellationToken);
        return ToResponse(passkey);
    }

    public async Task<Result<PasskeyResponse>> RenameAsync(Guid userId, string passkeyId, RenamePasskeyRequest request, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, passkeyId) is not var (user, passkey))
        {
            return AccountErrors.PasskeyNotFound;
        }

        passkey.Name = NameOf(request.Name);
        await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        await RecordAsync(AuditActions.UserPasskeyRenamed, user.Id, passkey, cancellationToken);
        return ToResponse(passkey);
    }

    public async Task<Result> RemoveAsync(Guid userId, string passkeyId, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, passkeyId) is not var (user, passkey))
        {
            return AccountErrors.PasskeyNotFound;
        }

        await userManager.RemovePasskeyAsync(user, passkey.CredentialId);
        await RecordAsync(AuditActions.UserPasskeyRemoved, user.Id, passkey, cancellationToken);
        return Result.Success();
    }

    public static PasskeyResponse ToResponse(UserPasskeyInfo passkey) =>
        new(Base64Url.EncodeToString(passkey.CredentialId), passkey.Name ?? "Passkey", passkey.CreatedAt, passkey.IsBackedUp);

    /// <summary>A name for people to tell passkeys apart: trimmed, at most 64 characters, and never blank.</summary>
    private static string NameOf(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? "Passkey" : trimmed[..Math.Min(trimmed.Length, NameMaxLength)];
    }

    private async Task<(User User, UserPasskeyInfo Passkey)?> FindAsync(Guid userId, string passkeyId)
    {
        if (!Base64Url.IsValid(passkeyId)
            || await userManager.FindByIdAsync(userId.ToString()) is not { } user
            || await userManager.GetPasskeyAsync(user, Base64Url.DecodeFromChars(passkeyId)) is not { } passkey)
        {
            return null;
        }

        return (user, passkey);
    }

    private async Task RecordAsync(string action, Guid userId, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        auditLog.Record(action, AuditSubject.User(userId), new Dictionary<string, object?>
        {
            ["passkey"] = Base64Url.EncodeToString(passkey.CredentialId),
            ["name"] = passkey.Name,
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
