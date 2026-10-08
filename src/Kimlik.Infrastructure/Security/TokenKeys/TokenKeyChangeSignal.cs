using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>
/// Fires when the token keys change. Options built from the keys (the OpenID Connect server options, and the
/// validation options that copy the server's keys) subscribe through <see cref="TokenKeyOptionsChangeTokenSource{TOptions}"/>,
/// so the options monitor rebuilds them with the new keys.
/// </summary>
public sealed class TokenKeyChangeSignal : IDisposable
{
    private CancellationTokenSource _source = new();

    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _source).Token);

    public void Dispose() => _source.Dispose();

    // The fired source is not disposed: subscribers may still be registering callbacks on its token.
    internal void NotifyChanged() => Interlocked.Exchange(ref _source, new CancellationTokenSource()).Cancel();
}

/// <summary>Rebuilds <typeparamref name="TOptions"/> whenever the token keys change.</summary>
public sealed class TokenKeyOptionsChangeTokenSource<TOptions>(TokenKeyChangeSignal signal) : IOptionsChangeTokenSource<TOptions>
{
    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => signal.GetChangeToken();
}
