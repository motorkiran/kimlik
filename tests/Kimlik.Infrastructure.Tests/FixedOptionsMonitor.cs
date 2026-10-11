using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Tests;

/// <summary>Options that never change, for code that reads them through a monitor.</summary>
internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
