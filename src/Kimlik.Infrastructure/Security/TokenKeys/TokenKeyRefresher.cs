namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>Refreshes the key ring and announces changes, so options built from the keys are rebuilt.</summary>
internal sealed class TokenKeyRefresher(TokenKeyRing keyRing, TokenKeyChangeSignal signal)
{
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await keyRing.RefreshAsync(cancellationToken))
        {
            return false;
        }

        signal.NotifyChanged();
        return true;
    }
}
