using Kimlik.Domain.Common;

namespace Kimlik.Application.Provisioning;

public enum ProvisioningChange
{
    Unchanged,
    Created,
    Updated,
}

public static class ProvisioningErrors
{
    /// <summary>Some properties, such as a client's type, identify what tokens and code rely on and cannot change.</summary>
    public static Error FixedProperty(string property) => Error.Validation(
        "provisioning.fixed_property", $"'{property}' cannot be changed once created; create a new one with another key instead.");

    /// <summary>The error of one item in the document, with where to find it.</summary>
    internal static Error At(string list, int index, string key, Error error) =>
        error with { Message = $"{list}[{index}] '{key}': {error.Message}" };
}

/// <summary>Compares declared values with stored ones the way Kimlik stores them.</summary>
internal static class Declared
{
    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool SameSet(IEnumerable<string> stored, IEnumerable<string> declared) =>
        stored.ToHashSet(StringComparer.Ordinal).SetEquals(declared);

    /// <summary>Compares URIs in their canonical form; a declared value that is not a URI never matches.</summary>
    public static bool SameUris(IEnumerable<string> stored, IEnumerable<string> declared) =>
        SameSet(stored.Select(Canonical), declared.Select(Canonical));

    private static string Canonical(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.AbsoluteUri : uri;
}
