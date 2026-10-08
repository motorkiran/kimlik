using System.Security.Cryptography;
using Kimlik.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Tests.Security;

public sealed class SecretProtectorTests
{
    private const string Purpose = "Kimlik.Tests.v1";
    private static readonly byte[] Secret = "correct horse battery staple"u8.ToArray();

    private readonly SecretProtector _protector = CreateProtector(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Unprotect_ReturnsOriginalSecret()
    {
        var protectedData = _protector.Protect(Secret, Purpose);

        _protector.Unprotect(protectedData, Purpose).ShouldBe(Secret);
    }

    [Fact]
    public void Protect_UsesFreshNonce_ForEveryCall()
    {
        _protector.Protect(Secret, Purpose).ShouldNotBe(_protector.Protect(Secret, Purpose));
    }

    [Fact]
    public void Unprotect_Fails_ForAnotherPurpose()
    {
        var protectedData = _protector.Protect(Secret, Purpose);

        Should.Throw<CryptographicException>(() => _protector.Unprotect(protectedData, "Kimlik.Tests.Other.v1"));
    }

    [Fact]
    public void Unprotect_Fails_WhenDataIsTampered()
    {
        var protectedData = _protector.Protect(Secret, Purpose);
        protectedData[^1] ^= 0x01;

        Should.Throw<CryptographicException>(() => _protector.Unprotect(protectedData, Purpose));
    }

    [Fact]
    public void Unprotect_Fails_WhenAssociatedDataDiffers()
    {
        var protectedData = _protector.Protect(Secret, Purpose, "record-1"u8);

        Should.Throw<CryptographicException>(() => _protector.Unprotect(protectedData, Purpose, "record-2"u8));
    }

    [Fact]
    public void Unprotect_Fails_WithAnotherMasterKey()
    {
        var protectedData = _protector.Protect(Secret, Purpose);
        var otherProtector = CreateProtector(RandomNumberGenerator.GetBytes(32));

        Should.Throw<CryptographicException>(() => otherProtector.Unprotect(protectedData, Purpose));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("AAAA")]
    public void IsValidMasterKey_RejectsKeysThatAreNot256Bits(string value)
    {
        SecurityOptions.IsValidMasterKey(value).ShouldBeFalse();
    }

    [Fact]
    public void IsValidMasterKey_AcceptsBase64Encoded256BitKey()
    {
        SecurityOptions.IsValidMasterKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).ShouldBeTrue();
    }

    internal static SecretProtector CreateProtector(byte[] masterKey) =>
        new(Options.Create(new SecurityOptions { MasterKey = Convert.ToBase64String(masterKey) }));
}
