using System.ComponentModel.DataAnnotations;

namespace Kimlik.Contracts.Account;

public sealed record ChangePasswordRequest
{
    [Required]
    [StringLength(128)]
    public required string CurrentPassword { get; init; }

    [Required]
    [StringLength(128)]
    public required string NewPassword { get; init; }
}

/// <summary>Deleting the account takes the password, so a stolen token alone cannot do it.</summary>
public sealed record DeleteAccountRequest
{
    [Required]
    [StringLength(128)]
    public required string Password { get; init; }
}

/// <summary>An application the user signed in to, which can get tokens on their behalf until the session is revoked.</summary>
public sealed record SessionResponse(Guid Id, string? ClientId, string? ClientName, IReadOnlyList<string> Scopes, DateTimeOffset? CreatedAt);
