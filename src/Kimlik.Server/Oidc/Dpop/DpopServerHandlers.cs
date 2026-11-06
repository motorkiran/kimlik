using System.Text.Json.Nodes;
using Kimlik.Security;
using Microsoft.AspNetCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Kimlik.Server.Oidc.Dpop;

/// <summary>
/// DPoP in OpenIddict's server, which does not implement it: bound access tokens from the token endpoint, the
/// <c>dpop_jkt</c> authorization parameter, and DPoP tokens at UserInfo. The token endpoint checks the proofs; see
/// <see cref="TokenEndpoint"/>.
/// </summary>
internal static class DpopServerHandlers
{
    public static OpenIddictServerBuilder AddDpop(this OpenIddictServerBuilder builder) => builder
        .AddEventHandler(AttachConfirmation.Descriptor)
        .AddEventHandler(AttachTokenType.Descriptor)
        .AddEventHandler(ValidateThumbprintParameter.Descriptor)
        .AddEventHandler(ExtractDpopToken.Descriptor)
        .AddEventHandler(ValidateBinding.Descriptor)
        .AddEventHandler(AttachChallenge.Descriptor)
        .AddEventHandler<HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            context.Metadata["dpop_signing_alg_values_supported"] = new OpenIddictParameter(System.Text.Json.JsonSerializer.SerializeToElement(DpopProof.Algorithms));
            return default;
        }));

    private static string? ThumbprintOf(OpenIddictServerTransaction transaction) =>
        transaction.Properties.TryGetValue(DpopBinding.ThumbprintProperty, out var value) ? value as string : null;

    /// <summary>Binds the access token to the key of the token request's proof (<c>cnf.jkt</c>).</summary>
    private sealed class AttachConfirmation : IOpenIddictServerHandler<ProcessSignInContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseSingletonHandler<AttachConfirmation>()
            // After OpenIddict prepared the access token's claims, which leaves cnf out, and before it signs them.
            .SetOrder(OpenIddictServerHandlers.PrepareAccessTokenPrincipal.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ProcessSignInContext context)
        {
            if (context.AccessTokenPrincipal is { } principal && ThumbprintOf(context.Transaction) is { } thumbprint)
            {
                principal.SetClaim(Claims.Confirmation, new JsonObject { ["jkt"] = thumbprint });
            }

            return default;
        }
    }

    /// <summary>Says <c>token_type: DPoP</c> for bound access tokens.</summary>
    private sealed class AttachTokenType : IOpenIddictServerHandler<ProcessSignInContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseSingletonHandler<AttachTokenType>()
            .SetOrder(OpenIddictServerHandlers.AttachSignInParameters.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ProcessSignInContext context)
        {
            if (context.Response.AccessToken is not null && ThumbprintOf(context.Transaction) is not null)
            {
                context.Response.TokenType = DpopProof.Scheme;
            }

            return default;
        }
    }

    /// <summary>
    /// Refuses a pushed authorization request whose <c>dpop_jkt</c> cannot be a key's thumbprint. The authorization page checks
    /// the requests that come through the browser.
    /// </summary>
    private sealed class ValidateThumbprintParameter : IOpenIddictServerHandler<ValidatePushedAuthorizationRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidatePushedAuthorizationRequestContext>()
            .UseSingletonHandler<ValidateThumbprintParameter>()
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ValidatePushedAuthorizationRequestContext context)
        {
            if (context.Request.GetParameter(DpopBinding.ThumbprintParameter) is { } parameter && !DpopBinding.IsThumbprint((string?)parameter))
            {
                context.Reject(Errors.InvalidRequest, DpopBinding.InvalidThumbprint);
            }

            return default;
        }
    }

    /// <summary>Reads the access token that UserInfo receives as <c>Authorization: DPoP</c>; OpenIddict reads bearer tokens only.</summary>
    private sealed class ExtractDpopToken : IOpenIddictServerHandler<ExtractUserInfoRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ExtractUserInfoRequestContext>()
            .UseSingletonHandler<ExtractDpopToken>()
            .SetOrder(OpenIddictServerAspNetCoreHandlers.ExtractAccessToken<ExtractUserInfoRequestContext>.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ExtractUserInfoRequestContext context)
        {
            if (context.Transaction.GetHttpRequest() is { } request && context.Request is { } message && DpopBinding.TokenOf(request) is { } token)
            {
                message.AccessToken = token;
            }

            return default;
        }
    }

    /// <summary>
    /// Holds access tokens bound to a key to a proof by it at UserInfo. OpenIddict refuses every <c>cnf</c> other than a
    /// certificate's, so this takes over for bound tokens. A client exchanging a bound token (RFC 8693) received it from
    /// the caller of its API, with a proof that its API checked, so the token endpoint takes it without one.
    /// </summary>
    private sealed class ValidateBinding(DpopProofValidator validator) : IOpenIddictServerHandler<ValidateTokenContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<ValidateBinding>()
            .SetOrder(OpenIddictServerHandlers.Protection.ValidateProofOfPossession.Descriptor.Order - 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public async ValueTask HandleAsync(ValidateTokenContext context)
        {
            if (context.DisableProofOfPossessionValidation || context.Principal is not { } principal || context.Token is not { } token
                || context.Transaction.GetHttpRequest() is not { } request)
            {
                return;
            }

            if (context.EndpointType == OpenIddictServerEndpointType.UserInfo
                && await DpopBinding.CheckAsync(validator, request, token, principal, request.HttpContext.RequestAborted) is var (error, description))
            {
                context.Reject(error, description);
                return;
            }

            if (DpopProof.BoundKeyOf(principal.GetClaim(Claims.Confirmation)) is not null)
            {
                context.DisableProofOfPossessionValidation = true;
            }
        }
    }

    /// <summary>Answers UserInfo requests with DPoP tokens with a DPoP challenge.</summary>
    private sealed class AttachChallenge : IOpenIddictServerHandler<ApplyUserInfoResponseContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyUserInfoResponseContext>()
            .UseSingletonHandler<AttachChallenge>()
            .SetOrder(OpenIddictServerAspNetCoreHandlers.AttachWwwAuthenticateHeader<ApplyUserInfoResponseContext>.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ApplyUserInfoResponseContext context)
        {
            if (context.Transaction.GetHttpRequest() is { } request)
            {
                DpopBinding.Challenge(request, context.Transaction.Response?.Error);
            }

            return default;
        }
    }
}
