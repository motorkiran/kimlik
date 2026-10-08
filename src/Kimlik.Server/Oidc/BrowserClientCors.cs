using Kimlik.Infrastructure.Clients;
using Microsoft.AspNetCore.Cors.Infrastructure;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kimlik.Server.Oidc;

internal static class BrowserClientCors
{
    public static IServiceCollection AddBrowserClientCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddSingleton<ICorsPolicyProvider, BrowserClientCorsPolicyProvider>();
        return services;
    }
}

/// <summary>
/// Lets browser apps call the protocol endpoints they use (discovery, keys, token, user info and revocation) from
/// the origins of the redirect URIs of public clients. Other origins and endpoints get no CORS headers, so browsers
/// keep blocking them; the Management API in particular is not meant for browsers.
/// </summary>
internal sealed class BrowserClientCorsPolicyProvider(IServiceScopeFactory scopeFactory, ClientChangeSignal clientChanges, TimeProvider timeProvider)
    : ICorsPolicyProvider, IDisposable
{
    private static readonly PathString[] Endpoints = ["/.well-known", "/connect/token", "/connect/userinfo", "/connect/revoke"];

    /// <summary>
    /// When an unknown origin shows up, such as that of a client another instance just registered, origins are
    /// reloaded at most this often. Changes made through this instance apply at once.
    /// </summary>
    private static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(5);

    /// <summary>Known origins are trusted this long, so a client removed through another instance lingers at most this much.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private volatile KnownOrigins _origins = new([], DateTimeOffset.MinValue, Version: -1);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (!Array.Exists(Endpoints, endpoint => context.Request.Path.StartsWithSegments(endpoint))
            || context.Request.Headers.Origin is not [{ Length: > 0 } origin])
        {
            return null;
        }

        return await IsAllowedAsync(origin, context.RequestAborted)
            ? new CorsPolicyBuilder()
                .WithOrigins(origin)
                .WithMethods(HttpMethods.Get, HttpMethods.Post)
                .WithHeaders("Authorization", "Content-Type")
                .SetPreflightMaxAge(TimeSpan.FromHours(1))
                .Build()
            : null;
    }

    public void Dispose() => _reloadLock.Dispose();

    private async Task<bool> IsAllowedAsync(string origin, CancellationToken cancellationToken)
    {
        var origins = _origins;
        if (IsCurrent(origins))
        {
            if (origins.Contains(origin))
            {
                return true;
            }

            if (timeProvider.GetUtcNow() - origins.LoadedAt < ReloadInterval)
            {
                return false;
            }
        }

        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have reloaded while this one waited.
            if (!IsCurrent(_origins) || (!_origins.Contains(origin) && timeProvider.GetUtcNow() - _origins.LoadedAt >= ReloadInterval))
            {
                _origins = await LoadAsync(cancellationToken);
            }

            return _origins.Contains(origin);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private bool IsCurrent(KnownOrigins origins) =>
        origins.Version == clientChanges.Version && timeProvider.GetUtcNow() - origins.LoadedAt < MaxAge;

    private async Task<KnownOrigins> LoadAsync(CancellationToken cancellationToken)
    {
        // Read first: a change committed while loading makes the result stale rather than lost.
        var version = clientChanges.Version;

        await using var scope = scopeFactory.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (!await applications.HasClientTypeAsync(application, ClientTypes.Public, cancellationToken))
            {
                continue;
            }

            var uris = (await applications.GetRedirectUrisAsync(application, cancellationToken))
                .Concat(await applications.GetPostLogoutRedirectUrisAsync(application, cancellationToken));

            foreach (var uri in uris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
                {
                    origins.Add(parsed.GetLeftPart(UriPartial.Authority));
                }
            }
        }

        return new KnownOrigins(origins, timeProvider.GetUtcNow(), version);
    }

    private sealed record KnownOrigins(HashSet<string> Origins, DateTimeOffset LoadedAt, long Version)
    {
        public bool Contains(string origin) => Origins.Contains(origin);
    }
}
