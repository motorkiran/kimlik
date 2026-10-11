using Kimlik.Contracts;
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
/// Lets browser apps call the protocol endpoints they use (discovery, keys, token, user info and revocation) from the
/// origins of the redirect URIs of public clients, and the Account API's organization endpoints, which Kimlik's
/// organization components call, from those of clients allowed the <c>kimlik.organizations</c> scope. Other origins and
/// endpoints get no CORS headers, so browsers keep blocking them; the Management API in particular is not meant for
/// browsers.
/// </summary>
internal sealed class BrowserClientCorsPolicyProvider(IServiceScopeFactory scopeFactory, ClientChangeSignal clientChanges, TimeProvider timeProvider)
    : ICorsPolicyProvider, IDisposable
{
    private static readonly PathString[] ProtocolEndpoints = ["/.well-known", "/connect/token", "/connect/userinfo", "/connect/revoke"];

    private static readonly PathString[] OrganizationEndpoints = ["/api/v1/me/organizations", "/api/v1/me/invitations"];

    /// <summary>
    /// When an unknown origin shows up, such as that of a client another instance just registered, origins are
    /// reloaded at most this often. Changes made through this instance apply at once.
    /// </summary>
    private static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(5);

    /// <summary>Known origins are trusted this long, so a client removed through another instance lingers at most this much.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private volatile KnownOrigins _origins = new([], [], DateTimeOffset.MinValue, Version: -1);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var path = context.Request.Path;
        var protocol = Array.Exists(ProtocolEndpoints, path.StartsWithSegments);
        if ((!protocol && !Array.Exists(OrganizationEndpoints, path.StartsWithSegments))
            || context.Request.Headers.Origin is not [{ Length: > 0 } origin]
            || !await IsAllowedAsync(origin, protocol, context.RequestAborted))
        {
            return null;
        }

        // Calls carry bearer tokens, never cookies, so no credentials are allowed.
        return new CorsPolicyBuilder()
            .WithOrigins(origin)
            .WithMethods(protocol ? [HttpMethods.Get, HttpMethods.Post] : [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete])
            .WithHeaders("Authorization", "Content-Type")
            .SetPreflightMaxAge(TimeSpan.FromHours(1))
            .Build();
    }

    public void Dispose() => _reloadLock.Dispose();

    private async Task<bool> IsAllowedAsync(string origin, bool protocol, CancellationToken cancellationToken)
    {
        var origins = _origins;
        if (IsCurrent(origins))
        {
            if (origins.Allows(origin, protocol))
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
            if (!IsCurrent(_origins) || (!_origins.Allows(origin, protocol) && timeProvider.GetUtcNow() - _origins.LoadedAt >= ReloadInterval))
            {
                _origins = await LoadAsync(cancellationToken);
            }

            return _origins.Allows(origin, protocol);
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
        var publicClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var organizationClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var isPublic = await applications.HasClientTypeAsync(application, ClientTypes.Public, cancellationToken);
            var managesOrganizations = await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + KimlikScopes.Organizations, cancellationToken);
            if (!isPublic && !managesOrganizations)
            {
                continue;
            }

            var uris = (await applications.GetRedirectUrisAsync(application, cancellationToken))
                .Concat(await applications.GetPostLogoutRedirectUrisAsync(application, cancellationToken));

            foreach (var uri in uris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
                {
                    var origin = parsed.GetLeftPart(UriPartial.Authority);
                    if (isPublic)
                    {
                        publicClients.Add(origin);
                    }

                    if (managesOrganizations)
                    {
                        organizationClients.Add(origin);
                    }
                }
            }
        }

        return new KnownOrigins(publicClients, organizationClients, timeProvider.GetUtcNow(), version);
    }

    /// <param name="PublicClients">Origins that may call the protocol endpoints.</param>
    /// <param name="OrganizationClients">Origins that may call the organization endpoints.</param>
    /// <param name="LoadedAt">When they were read.</param>
    /// <param name="Version">The client changes they include, as <see cref="ClientChangeSignal.Version"/> counts them.</param>
    private sealed record KnownOrigins(HashSet<string> PublicClients, HashSet<string> OrganizationClients, DateTimeOffset LoadedAt, long Version)
    {
        public bool Allows(string origin, bool protocol) => (protocol ? PublicClients : OrganizationClients).Contains(origin);
    }
}
