namespace Kimlik.Server.Captcha;

/// <summary>The CAPTCHA service Kimlik puts on its forms, chosen with <c>Kimlik:Captcha:Provider</c>.</summary>
public enum CaptchaProvider
{
    /// <summary>No CAPTCHA: rate limits and lockouts alone guard the forms.</summary>
    None,
    Turnstile,
    HCaptcha,

    /// <summary>reCAPTCHA v2, the "I'm not a robot" checkbox.</summary>
    Recaptcha,
}

/// <summary>A form a CAPTCHA can guard.</summary>
public enum CaptchaForm
{
    SignUp,
    SignIn,
    PasswordReset,

    /// <summary>Asking for a sign-in code, by email or text message.</summary>
    SignInCode,
}

/// <summary>Bot protection on the hosted forms, from the <c>Kimlik:Captcha</c> section.</summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Kimlik:Captcha";

    public static readonly IReadOnlyList<CaptchaForm> DefaultForms = [CaptchaForm.SignUp, CaptchaForm.PasswordReset, CaptchaForm.SignInCode];

    public CaptchaProvider Provider { get; set; }

    /// <summary>The site key, which the widget shows on the page.</summary>
    public string? SiteKey { get; set; }

    /// <summary>The secret key, with which Kimlik checks answers; keep it in a secret store.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The forms to guard; sign-up, password reset and sign-in codes when unset.</summary>
    public List<CaptchaForm>? Forms { get; set; }

    public bool Enabled => Provider != CaptchaProvider.None;

    public bool Guards(CaptchaForm form) => Enabled && (Forms ?? DefaultForms).Contains(form);
}
