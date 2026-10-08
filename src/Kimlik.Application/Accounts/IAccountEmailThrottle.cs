namespace Kimlik.Application.Accounts;

/// <summary>
/// Limits how often one account receives the same kind of email, so public forms cannot be used to flood
/// someone's inbox, even from many addresses.
/// </summary>
public interface IAccountEmailThrottle
{
    /// <returns><see langword="false"/> when an email of this kind was sent to the account moments ago.</returns>
    bool TryAcquire(Guid userId, AccountEmail kind);
}
