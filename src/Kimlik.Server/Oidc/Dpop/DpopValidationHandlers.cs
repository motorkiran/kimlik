using Microsoft.AspNetCore;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Validation.OpenIddictValidationEvents;

namespace Kimlik.Server.Oidc.Dpop;

/// <summary>DPoP tokens at Kimlik's API, which OpenIddict's validation reads only as bearer tokens.</summary>
internal static class DpopValidationHandlers
{
    public static OpenIddictValidationBuilder AddDpop(this OpenIddictValidationBuilder builder) => builder
        .AddEventHandler(ExtractDpopToken.Descriptor)
        .AddEventHandler(ValidateBinding.Descriptor)
        .AddEventHandler(AttachChallenge.Descriptor);

    private sealed class ExtractDpopToken : IOpenIddictValidationHandler<ProcessAuthenticationContext>
    {
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; } = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
            .UseSingletonHandler<ExtractDpopToken>()
            .SetOrder(OpenIddictValidationAspNetCoreHandlers.ExtractAccessTokenFromAuthorizationHeader.Descriptor.Order - 1)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            if (string.IsNullOrEmpty(context.AccessToken) && context.Transaction.GetHttpRequest() is { } request && DpopBinding.TokenOf(request) is { } token)
            {
                context.AccessToken = token;
            }

            return default;
        }
    }

    /// <summary>Holds access tokens bound to a key to a proof by it; OpenIddict refuses every <c>cnf</c> other than a certificate's.</summary>
    private sealed class ValidateBinding(DpopProofValidator validator) : IOpenIddictValidationHandler<ValidateTokenContext>
    {
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; } = OpenIddictValidationHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<ValidateBinding>()
            .SetOrder(OpenIddictValidationHandlers.Protection.ValidateProofOfPossession.Descriptor.Order - 1)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

        public async ValueTask HandleAsync(ValidateTokenContext context)
        {
            if (context.DisableProofOfPossessionValidation || context.Principal is not { } principal || context.Token is not { } token
                || context.Transaction.GetHttpRequest() is not { } request)
            {
                return;
            }

            if (await DpopBinding.CheckAsync(validator, request, token, principal, request.HttpContext.RequestAborted) is var (error, description))
            {
                context.Reject(error, description);
                return;
            }

            // Checked above; OpenIddict would refuse the confirmation as unknown.
            context.DisableProofOfPossessionValidation = true;
        }
    }

    private sealed class AttachChallenge : IOpenIddictValidationHandler<ProcessChallengeContext>
    {
        public static OpenIddictValidationHandlerDescriptor Descriptor { get; } = OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessChallengeContext>()
            .UseSingletonHandler<AttachChallenge>()
            .SetOrder(OpenIddictValidationAspNetCoreHandlers.AttachWwwAuthenticateHeader<ProcessChallengeContext>.Descriptor.Order + 1)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

        public ValueTask HandleAsync(ProcessChallengeContext context)
        {
            if (context.Transaction.GetHttpRequest() is { } request)
            {
                DpopBinding.Challenge(request, context.Transaction.Response?.Error);
            }

            return default;
        }
    }
}
