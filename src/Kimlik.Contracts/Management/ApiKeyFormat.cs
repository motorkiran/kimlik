using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Kimlik.Contracts.Management;

/// <summary>
/// What API key secrets look like: <c>kmk_</c> and 43 base64url characters, 256 random bits. The prefix lets secret
/// scanners and people recognize them.
/// </summary>
public static class ApiKeyFormat
{
    public const string Prefix = "kmk_";

    public const int Length = 47;

    /// <summary>Whether the value could be a key at all, so that anything else is turned away without a lookup.</summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? value) =>
        value is { Length: Length } && value.StartsWith(Prefix, StringComparison.Ordinal) && Base64Url.IsValid(value.AsSpan(Prefix.Length));
}
