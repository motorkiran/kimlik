namespace Kimlik.Application.Plans;

/// <summary>From the <c>Kimlik:Plans</c> configuration section.</summary>
public sealed class PlanOptions
{
    public const string SectionName = "Kimlik:Plans";

    /// <summary>The key of the plan of users without a current subscription.</summary>
    public string? DefaultUserPlan { get; set; }

    /// <summary>The key of the plan of organizations without a current subscription.</summary>
    public string? DefaultOrganizationPlan { get; set; }

    /// <summary>How often ended subscriptions are marked expired. Entitlements never wait for it.</summary>
    public TimeSpan ExpirationInterval { get; set; } = TimeSpan.FromMinutes(5);
}
