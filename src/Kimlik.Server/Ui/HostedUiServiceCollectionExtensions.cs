using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Kimlik.Server.Hosting;
using Microsoft.AspNetCore.Localization;

namespace Kimlik.Server.Ui;

internal static class HostedUiServiceCollectionExtensions
{
    public static readonly CultureInfo[] SupportedCultures = [new("en"), new("tr")];

    /// <summary>The hosted pages: Razor Pages and localization.</summary>
    public static IServiceCollection AddHostedUi(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        // Write Turkish and other non-Latin text as UTF-8 instead of numeric entities; characters that are
        // special in HTML are still encoded.
        services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.AddRazorPages(options => options.Conventions.ConfigureFilter(new SecurityHeaders.ContentSecurityPolicyFilter()))
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(options =>
                options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;
            options.RequestCultureProviders =
            [
                new UiLocalesRequestCultureProvider(),
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        return services;
    }

    /// <summary>Remembers the language a person picks in the page footer.</summary>
    public static IEndpointRouteBuilder MapCultureSwitch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/culture/{culture}", (string culture, string? returnUrl, HttpContext context) =>
        {
            if (Array.Exists(SupportedCultures, supported => supported.Name == culture))
            {
                context.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(365) });
            }

            return Results.LocalRedirect(returnUrl is { Length: > 0 } && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) ? returnUrl : "/");
        }).ExcludeFromDescription();

        return endpoints;
    }
}
