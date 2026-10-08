using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Account;

/// <summary>
/// A passkey the user signs in with. <c>Id</c> is its credential ID in base64url; <c>Synced</c> passkeys are backed up
/// by a password manager or the device's platform, and work on the user's other devices too.
/// </summary>
public sealed record PasskeyResponse(string Id, string Name, DateTimeOffset CreatedAt, bool Synced);

public sealed record RenamePasskeyRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Name { get; init; }
}
