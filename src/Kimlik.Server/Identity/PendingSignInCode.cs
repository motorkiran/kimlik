using System.Security.Cryptography;
using System.Text.Json;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>Where a sign-in code goes: to an email address, or by text message to a phone number.</summary>
public enum SignInCodeChannel
{
    Email,
    Sms,
}

/// <summary>
/// The address or phone number someone asked a sign-in code for, and where the sign-in goes, kept in a short-lived
/// cookie until they enter the code, or open the link sent with an emailed one in this browser. It is protected, expires
/// after fifteen minutes, and says nothing about whether an account has the address or number.
/// </summary>
public sealed class PendingSignInCode(IDataProtectionProvider dataProtection, IOptions<ServerOptions> server)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>A sign-in waiting for its code; <c>Address</c> is the email address, or the phone number in E.164.</summary>
    public sealed record Pending(SignInCodeChannel Channel, string Address, bool Persistent, string? ReturnUrl);

    private string CookieName => server.Value.RequireHttps ? "__Host-kimlik.sign-in-code" : "kimlik.sign-in-code";

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector("Kimlik.SignInCode.Pending").ToTimeLimitedDataProtector();

    public void Start(HttpContext context, SignInCodeChannel channel, string address, bool persistent, string? returnUrl) => context.Response.Cookies.Append(
        CookieName,
        Protector.Protect(JsonSerializer.Serialize(new Pending(channel, address, persistent, AccountLinks.IsLocalUrl(returnUrl) ? returnUrl : null)), Lifetime),
        Options(context));

    public Pending? Read(HttpContext context)
    {
        if (context.Request.Cookies[CookieName] is not { Length: > 0 } value)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Pending>(Protector.Unprotect(value));
        }
        catch (CryptographicException)
        {
            // Expired or tampered with.
            return null;
        }
    }

    public void End(HttpContext context) => context.Response.Cookies.Delete(CookieName, Options(context));

    private CookieOptions Options(HttpContext context) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Lax,
        Secure = server.Value.RequireHttps || context.Request.IsHttps,
        MaxAge = Lifetime,
    };
}
