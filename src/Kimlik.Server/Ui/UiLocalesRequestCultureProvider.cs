using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Ui;

/// <summary>
/// Honors the OpenID Connect <c>ui_locales</c> parameter, both on the authorization request itself and on the
/// pages it leads to, which carry the authorization request in their return URL.
/// </summary>
internal sealed class UiLocalesRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var uiLocales = httpContext.Request.Query[Parameters.UiLocales].ToString();

        if (string.IsNullOrEmpty(uiLocales)
            && httpContext.Request.Query["ReturnUrl"].ToString() is { Length: > 0 } returnUrl
            && returnUrl.IndexOf('?', StringComparison.Ordinal) is var queryStart and >= 0)
        {
            uiLocales = QueryHelpers.ParseQuery(returnUrl[queryStart..]).TryGetValue(Parameters.UiLocales, out var values)
                ? values.ToString()
                : string.Empty;
        }

        var locales = uiLocales.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(locale => new StringSegment(locale)).ToList();
        return Task.FromResult(locales.Count == 0 ? null : new ProviderCultureResult(locales, locales));
    }
}
