using System.Security.Cryptography;

namespace Kimlik.Server.Tests;

/// <summary>Configuration shared by every in-memory Kimlik host in the tests.</summary>
internal static class TestConfiguration
{
    public const string Environment = "Testing";

    /// <summary>A fresh master key per test run; nothing encrypted by the tests outlives it.</summary>
    public static readonly string MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static Dictionary<string, string?> Create(string connectionString) => new()
    {
        ["ConnectionStrings:Kimlik"] = connectionString,
        ["Kimlik:Security:MasterKey"] = MasterKey,
    };
}
