using System.Text;
using Kimlik.Infrastructure.Identity;

namespace Kimlik.Infrastructure.Tests.Identity;

public sealed class TotpTests
{
    // The SHA-1 seed of RFC 6238, appendix B, and its Base32 form.
    private static readonly byte[] Seed = Encoding.ASCII.GetBytes("12345678901234567890");
    private const string SeedBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59, 287082)]
    [InlineData(1111111109, 081804)]
    [InlineData(1111111111, 050471)]
    [InlineData(1234567890, 005924)]
    [InlineData(2000000000, 279037)]
    public void Codes_MatchTheRfcTestVectors(long unixTime, int expected)
    {
        Totp.Compute(Seed, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixTime))).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SeedBase32)]
    [InlineData("gezd gnbv gy3t qojq gezd gnbv gy3t qojq")]
    [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ====")]
    public void Keys_DecodeFromBase32_HoweverTheyAreWritten(string key)
    {
        Base32.Decode(key).ShouldBe(Seed);
    }

    [Fact]
    public void InvalidBase32_IsRejected()
    {
        Base32.Decode("NOT-BASE32!").ShouldBeNull();
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("123 456", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    [InlineData("", false)]
    public void Codes_AreSixDigits(string input, bool valid)
    {
        Totp.TryParseCode(input, out _).ShouldBe(valid);
    }
}
