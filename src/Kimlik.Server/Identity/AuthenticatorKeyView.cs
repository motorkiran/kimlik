using Kimlik.Contracts.Account;
using QRCoder;

namespace Kimlik.Server.Identity;

/// <summary>An authenticator key as a page shows it: a QR code to scan, and the key in groups of four to type.</summary>
public sealed record AuthenticatorKeyView(string Secret, string QrCode)
{
    public static AuthenticatorKeyView For(AuthenticatorSetupResponse setup)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(setup.OtpAuthUri, QRCodeGenerator.ECCLevel.M);

        return new AuthenticatorKeyView(
            string.Join(' ', setup.Secret.Chunk(4).Select(group => new string(group))),
            $"data:image/png;base64,{Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(5))}");
    }
}
