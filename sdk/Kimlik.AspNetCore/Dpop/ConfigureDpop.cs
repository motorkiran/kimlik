using System.Collections.Concurrent;
using System.Text.Json;
using Kimlik.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;

namespace Kimlik.AspNetCore.Dpop;

/// <summary>
/// Takes access tokens bound to a client's key as <c>Authorization: DPoP</c>, with a proof by that key (RFC 9449), and
/// refuses them as bearer tokens; JwtBearer reads bearer tokens only. It goes in front of the events the application set
/// for the scheme, such as reading tokens from the query string for SignalR, which keep working.
/// </summary>
internal sealed class ConfigureDpop(DpopProofReplay replay, TimeProvider timeProvider) : IPostConfigureOptions<JwtBearerOptions>
{
    private const string SchemePrefix = DpopProof.Scheme + " ";

    private static readonly object FailureKey = new();

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != KimlikDefaults.AuthenticationScheme)
        {
            return;
        }

        // Events from the container would replace these, and bound tokens would pass as bearer tokens.
        if (options.EventsType is not null)
        {
            throw new InvalidOperationException(
                $"Kimlik takes DPoP tokens through JwtBearerOptions.Events; set Events, not EventsType, for the {KimlikDefaults.AuthenticationScheme} scheme.");
        }

        var events = options.Events ??= new JwtBearerEvents();

        var messageReceived = events.OnMessageReceived;
        events.OnMessageReceived = context =>
        {
            if (TokenOf(context.Request) is { } token)
            {
                context.Token = token;
            }

            return messageReceived(context);
        };

        var tokenValidated = events.OnTokenValidated;
        events.OnTokenValidated = context =>
        {
            CheckBinding(context);
            return context.Result is null ? tokenValidated(context) : Task.CompletedTask;
        };

        var challenge = events.OnChallenge;
        events.OnChallenge = context => Challenge(context) ? Task.CompletedTask : challenge(context);
    }

    private void CheckBinding(TokenValidatedContext context)
    {
        var request = context.Request;
        var boundTo = context.SecurityToken is JsonWebToken token && token.TryGetPayloadValue<JsonElement>("cnf", out var confirmation)
            ? DpopProof.BoundKeyOf(confirmation.GetRawText())
            : null;

        if (TokenOf(request) is not { } accessToken)
        {
            if (boundTo is not null)
            {
                Fail(context, "invalid_token", "The access token is bound to a key. Send it as a DPoP token, with a proof.");
            }

            return;
        }

        if (boundTo is null)
        {
            Fail(context, "invalid_token", "The access token is not bound to a key. Send it as a bearer token.");
            return;
        }

        var target = new Uri(UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path));
        var proof = DpopProof.Read(request.Headers[DpopProof.HeaderName], request.Method, target, accessToken, timeProvider.GetUtcNow(), out var error);
        if (proof is null)
        {
            Fail(context, DpopProof.InvalidProofError, error ?? "Send a DPoP proof with the access token.");
        }
        else if (!string.Equals(proof.Thumbprint, boundTo, StringComparison.Ordinal))
        {
            Fail(context, DpopProof.InvalidProofError, "The DPoP proof is not signed by the key the access token is bound to.");
        }
        else if (!replay.TryUse(proof))
        {
            Fail(context, DpopProof.InvalidProofError, "The DPoP proof was used before.");
        }
    }

    /// <summary>
    /// Answers requests with DPoP tokens, and refused proofs, with a DPoP challenge (RFC 9449, section 7.1); <see langword="false"/>
    /// leaves the challenge to JwtBearer.
    /// </summary>
    private static bool Challenge(JwtBearerChallengeContext context)
    {
        var failure = context.HttpContext.Items[FailureKey] as (string Error, string Description)?;
        if (TokenOf(context.Request) is null && failure?.Error != DpopProof.InvalidProofError)
        {
            return false;
        }

        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var challenge = $"{DpopProof.Scheme} algs=\"{string.Join(' ', DpopProof.Algorithms)}\"";
        context.Response.Headers[HeaderNames.WWWAuthenticate] = failure is var (error, description)
            ? $"{challenge}, error=\"{error}\", error_description=\"{description}\""
            : challenge;
        return true;
    }

    private static string? TokenOf(HttpRequest request) =>
        request.Headers.Authorization is [{ } header] && header.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase) && header.Length > SchemePrefix.Length
            ? header[SchemePrefix.Length..].Trim()
            : null;

    private static void Fail(TokenValidatedContext context, string error, string description)
    {
        context.HttpContext.Items[FailureKey] = (error, description);
        context.Fail(description);
    }
}

/// <summary>
/// The proofs this process accepted in the last five minutes, so that none is accepted twice. Processes do not share
/// them: with several, the short window and TLS do the rest.
/// </summary>
internal sealed class DpopProofReplay(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);
    private long _nextSweep;

    public bool TryUse(DpopProof proof)
    {
        var now = timeProvider.GetUtcNow();
        var nextSweep = Interlocked.Read(ref _nextSweep);
        if (now.UtcTicks >= nextSweep && Interlocked.CompareExchange(ref _nextSweep, (now + DpopProof.MaxAge).UtcTicks, nextSweep) == nextSweep)
        {
            foreach (var used in _used)
            {
                if (used.Value <= now)
                {
                    _used.TryRemove(used);
                }
            }
        }

        return _used.TryAdd(proof.ReplayKey, proof.UsableUntil);
    }
}
