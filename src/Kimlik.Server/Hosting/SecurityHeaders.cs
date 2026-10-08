using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kimlik.Server.Hosting;

internal static class SecurityHeaders
{
    private const string NonceItemKey = "Kimlik.CspNonce";

    /// <summary>Headers that are safe for every response.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        // Sign-in URLs carry return URLs and authorization request parameters; never leak them to other sites.
        headers["Referrer-Policy"] = "no-referrer";
        return next(context);
    });

    /// <summary>The nonce that allows this request's inline style element under the content security policy.</summary>
    public static string GetCspNonce(this HttpContext context)
    {
        if (context.Items[NonceItemKey] is not string nonce)
        {
            nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItemKey] = nonce;
        }

        return nonce;
    }

    /// <summary>
    /// Adds a strict content security policy to pages Kimlik renders. It is not applied to protocol responses:
    /// OpenIddict's <c>form_post</c> response relies on an inline script to return to the client. Pages run no script,
    /// except those marked with <see cref="RunsScriptsAttribute"/>, which run Kimlik's own scripts with the request's nonce.
    /// </summary>
    internal sealed class ContentSecurityPolicyFilter : IAsyncResultFilter
    {
        public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is PageResult)
            {
                var nonce = context.HttpContext.GetCspNonce();
                var scripts = context.ActionDescriptor.EndpointMetadata.OfType<RunsScriptsAttribute>().Any() ? $"'nonce-{nonce}'" : "'none'";
                context.HttpContext.Response.Headers.ContentSecurityPolicy =
                    $"default-src 'self'; script-src {scripts}; style-src 'self' 'nonce-{nonce}'; img-src 'self' data: https:; "
                    + "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
            }

            return next();
        }
    }
}

/// <summary>Marks a page that runs Kimlik's own scripts, such as the passkey one, loaded with the request's CSP nonce.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class RunsScriptsAttribute : Attribute;
