namespace Kimlik.AspNetCore.Entitlements;

/// <summary>
/// What a caller's plan gives: whether a feature is on, and the maximum of a limit. The plan comes from the access
/// token; feature values come from plan definitions that Kimlik.Client fetches and caches, or, for subscribers with
/// add-ons or overrides, from their own entitlements.
/// </summary>
public interface IKimlikEntitlements
{
    /// <summary>Whether the caller's plan turns the boolean feature on. Unknown features, and callers without a plan, get no.</summary>
    Task<bool> HasFeatureAsync(KimlikUser caller, string feature, CancellationToken cancellationToken = default);

    /// <summary>The maximum the caller's plan allows; <see langword="null"/> means unlimited, and an unknown limit is zero.</summary>
    Task<long?> GetLimitAsync(KimlikUser caller, string feature, CancellationToken cancellationToken = default);
}
