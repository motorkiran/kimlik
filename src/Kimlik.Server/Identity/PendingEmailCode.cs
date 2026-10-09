using System.Security.Cryptography;
using System.Text.Json;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Identity;

/// <summary>
/// The address someone asked a sign-in code for, and where the sign-in goes, kept in a short-lived cookie until they
/// enter the code, or open the link sent with it in this browser. It is protected, expires after fifteen minutes, and
/// says nothing about whether an account has the address.
/// </summary>
public sealed class PendingEmailCode(IDataProtectionProvider dataProtection, IOptions<ServerOptions> server)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public sealed record Pending(string Email, bool Persistent, string? ReturnUrl);

    private string CookieName => server.Value.RequireHttps ? "__Host-kimlik.email-code" : "kimlik.email-code";

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector("Kimlik.EmailSignIn.Pending").ToTimeLimitedDataProtector();

    public void Start(HttpContext context, string email, bool persistent, string? returnUrl) => context.Response.Cookies.Append(
        CookieName,
        Protector.Protect(JsonSerializer.Serialize(new Pending(email, persistent, AccountLinks.IsLocalUrl(returnUrl) ? returnUrl : null)), Lifetime),
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
