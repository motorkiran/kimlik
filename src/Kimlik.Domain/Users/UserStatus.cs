namespace Kimlik.Domain.Users;

public enum UserStatus
{
    Active,

    /// <summary>Blocked by an administrator: the user cannot sign in and existing tokens are revoked.</summary>
    Suspended,
}
