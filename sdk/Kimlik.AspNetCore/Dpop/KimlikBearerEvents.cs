using System.Collections.Concurrent;
using System.Text.Json;
using Kimlik.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;

namespace Kimlik.AspNetCore.Dpop;

/// <summary>
/// Takes access tokens bound to a client's key as <c>Authorization: DPoP</c>, with a proof by that key (RFC 9449), and
/// refuses them as bearer tokens; JwtBearer reads bearer tokens only.
/// </summary>
internal sealed class KimlikBearerEvents(DpopProofReplay replay, TimeProvider timeProvider) : JwtBearerEvents
{
    private const string SchemePrefix = DpopProof.Scheme + " ";

    private static readonly object FailureKey = new();

    public override Task MessageReceived(MessageReceivedContext context)
    {
        if (TokenOf(context.Request) is { } token)
        {
            context.Token = token;
        }

        return base.MessageReceived(context);
    }

    public override Task TokenValidated(TokenValidatedContext context)
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

            return Task.CompletedTask;
        }

        if (boundTo is null)
        {
            Fail(context, "invalid_token", "The access token is not bound to a key. Send it as a bearer token.");
            return Task.CompletedTask;
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

        return Task.CompletedTask;
    }

    /// <summary>Answers requests with DPoP tokens, and refused proofs, with a DPoP challenge (RFC 9449, section 7.1).</summary>
    public override Task Challenge(JwtBearerChallengeContext context)
    {
        var failure = context.HttpContext.Items[FailureKey] as (string Error, string Description)?;
        if (TokenOf(context.Request) is null && failure?.Error != DpopProof.InvalidProofError)
        {
            return base.Challenge(context);
        }

        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var challenge = $"{DpopProof.Scheme} algs=\"{string.Join(' ', DpopProof.Algorithms)}\"";
        context.Response.Headers[HeaderNames.WWWAuthenticate] = failure is var (error, description)
            ? $"{challenge}, error=\"{error}\", error_description=\"{description}\""
            : challenge;
        return Task.CompletedTask;
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
