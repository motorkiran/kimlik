using Kimlik.AspNetCore.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.AspNetCore.Authorization;

/// <summary>Requires the caller's plan to turn a boolean feature on, such as <c>export_pdf</c>.</summary>
public sealed class FeatureRequirement(string feature) : IAuthorizationRequirement
{
    public string Feature { get; } = feature;
}

internal sealed class FeatureAuthorizationHandler(IServiceProvider services) : AuthorizationHandler<FeatureRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FeatureRequirement requirement)
    {
        var entitlements = services.GetService<IKimlikEntitlements>()
            ?? throw new InvalidOperationException("RequireFeature needs the plan definitions: call AddKimlikEntitlements.");

        if (KimlikUser.FromPrincipal(context.User) is { } caller && await entitlements.HasFeatureAsync(caller, requirement.Feature))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Requires the caller's plan to turn <see cref="Feature"/> on, for MVC controllers and actions.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireFeatureAttribute(string feature) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Feature { get; } = feature;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new FeatureRequirement(Feature)];
}

public static class FeatureEndpointExtensions
{
    /// <summary>Requires the caller's plan to turn the boolean <paramref name="feature"/> on.</summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string feature)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(new RequireFeatureAttribute(feature));
}
