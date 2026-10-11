using System.Text.Json;
using Kimlik.Application.Captcha;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Captcha;

/// <summary>
/// What the page and Kimlik need of each CAPTCHA service: its script and widget, the form field of the widget's answer,
/// the endpoint that checks answers, and the origins its widget loads from, for the content security policy. All three
/// check answers the same way: a form post of the secret, the answer and the caller's address to <c>siteverify</c>,
/// which answers with <c>success</c>.
/// </summary>
internal sealed record CaptchaService(string ScriptUrl, string WidgetClass, string ResponseField, Uri VerifyUrl, string Sources, bool StylesAndConnections)
{
    public static CaptchaService For(CaptchaProvider provider) => provider switch
    {
        CaptchaProvider.Turnstile => new(
            "https://challenges.cloudflare.com/turnstile/v0/api.js",
            "cf-turnstile",
            "cf-turnstile-response",
            new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify"),
            "https://challenges.cloudflare.com",
            StylesAndConnections: false),
        CaptchaProvider.HCaptcha => new(
            "https://js.hcaptcha.com/1/api.js",
            "h-captcha",
            "h-captcha-response",
            new Uri("https://api.hcaptcha.com/siteverify"),
            "https://hcaptcha.com https://*.hcaptcha.com",
            StylesAndConnections: true),
        CaptchaProvider.Recaptcha => new(
            "https://www.google.com/recaptcha/api.js",
            "g-recaptcha",
            "g-recaptcha-response",
            new Uri("https://www.google.com/recaptcha/api/siteverify"),
            "https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/ https://recaptcha.google.com/recaptcha/",
            StylesAndConnections: false),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No CAPTCHA service."),
    };
}

/// <summary>Checks the CAPTCHA answers that the hosted forms post.</summary>
public sealed partial class CaptchaVerifier(IHttpClientFactory httpClients, IOptionsMonitor<CaptchaOptions> options, ILogger<CaptchaVerifier> logger)
{
    public const string HttpClientName = "Kimlik.Captcha";

    /// <summary>Longer than any answer the providers make (Turnstile's are at most 2048 characters), so nothing longer is sent on.</summary>
    private const int MaxAnswerLength = 4096;

    public bool Guards(CaptchaForm form) => options.CurrentValue.Guards(form);

    /// <summary>
    /// Whether the request may go on: the form is not guarded, or the provider accepted the widget's answer. A missing,
    /// oversized or refused answer stops it, as does any answer the provider gives that is not a success. Only a provider
    /// that cannot be reached, times out or fails on its side lets the form through, so an outage does not lock people
    /// out.
    /// </summary>
    public async Task<bool> PassesAsync(CaptchaForm form, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Guards(form))
        {
            return true;
        }

        var service = CaptchaService.For(options.CurrentValue.Provider);
        if (!context.Request.HasFormContentType || context.Request.Form[service.ResponseField].ToString() is not { Length: > 0 and <= MaxAnswerLength } answer)
        {
            return false;
        }

        var fields = new List<KeyValuePair<string, string>> { new("secret", options.CurrentValue.SecretKey!), new("response", answer) };
        if (context.Connection.RemoteIpAddress is { } address)
        {
            fields.Add(new("remoteip", address.ToString()));
        }

        HttpResponseMessage response;
        try
        {
            using var content = new FormUrlEncodedContent(fields);
            response = await httpClients.CreateClient(HttpClientName).PostAsync(service.VerifyUrl, content, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, options.CurrentValue.Provider, exception);
            return true;
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                LogUnreachable(logger, options.CurrentValue.Provider, new HttpRequestException($"The provider answered {(int)response.StatusCode}.", null, response.StatusCode));
                return true;
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                return response.IsSuccessStatusCode && result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    [LoggerMessage(LogLevel.Warning, "The {Provider} CAPTCHA could not be checked, so the form went through without it")]
    private static partial void LogUnreachable(ILogger logger, CaptchaProvider provider, Exception exception);
}

/// <summary>Marks a page that can show the CAPTCHA widget, so that its content security policy lets the widget load.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ShowsCaptchaAttribute : Attribute;
