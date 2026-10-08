namespace Kimlik.Application.Organizations;

/// <summary>From the <c>Kimlik:Organizations</c> configuration section.</summary>
public sealed class OrganizationOptions
{
    public const string SectionName = "Kimlik:Organizations";

    /// <summary>How long an invitation can be accepted; sending it again starts the period anew.</summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);
}
