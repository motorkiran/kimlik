using Kimlik.Client;
using Kimlik.Contracts.Management;
using Microsoft.Extensions.DependencyInjection;

namespace Kimlik.AspNetCore.Entitlements;

/// <summary>
/// Use of metered limits, such as API calls a month, for the caller's subscriber: its organization in an organization
/// context, otherwise the user. Use counts by calendar month, in UTC.
/// </summary>
public interface IKimlikUsage
{
    /// <summary>
    /// Uses <paramref name="quantity"/> of the month's allowance if it is still there, in one step; <see langword="false"/>
    /// when the limit is reached, and nothing is recorded then.
    /// </summary>
    Task<bool> TryConsumeAsync(KimlikUser caller, string feature, long quantity = 1, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    /// <summary>Records use that happened, whatever the limit, and returns the month's use.</summary>
    Task<UsageResponse> RecordAsync(KimlikUser caller, string feature, long quantity = 1, string? idempotencyKey = null, CancellationToken cancellationToken = default);
}

/// <summary>Records use through <see cref="KimlikClient"/>, resolved for each call as the entitlements are.</summary>
internal sealed class KimlikUsage(IServiceProvider services) : IKimlikUsage
{
    public async Task<bool> TryConsumeAsync(
        KimlikUser caller, string feature, long quantity = 1, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(caller, feature, quantity, idempotencyKey, enforce: true, cancellationToken);
            return true;
        }
        catch (KimlikApiException exception) when (exception.Code == "usage.limit_reached")
        {
            return false;
        }
    }

    public Task<UsageResponse> RecordAsync(
        KimlikUser caller, string feature, long quantity = 1, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        SendAsync(caller, feature, quantity, idempotencyKey, enforce: false, cancellationToken);

    private Task<UsageResponse> SendAsync(KimlikUser caller, string feature, long quantity, string? idempotencyKey, bool enforce, CancellationToken cancellationToken)
    {
        var (type, id) = KimlikSubscribers.Of(caller)
            ?? throw new InvalidOperationException("The caller is a service client, which has no plan and no use of its own.");
        return services.GetRequiredService<KimlikClient>().Usage.RecordAsync(
            new RecordUsageRequest
            {
                SubscriberType = type,
                SubscriberId = id,
                Feature = feature,
                Quantity = quantity,
                Enforce = enforce,
                IdempotencyKey = idempotencyKey,
            },
            cancellationToken);
    }
}

/// <summary>Whose plan and use a caller has.</summary>
internal static class KimlikSubscribers
{
    /// <summary>The caller's organization in an organization context, otherwise the user; nothing for a service client.</summary>
    public static (SubscriberType Type, Guid Id)? Of(KimlikUser caller) =>
        caller.OrganizationId is { } organizationId ? (SubscriberType.Organization, organizationId)
        : caller.UserId is { } userId ? (SubscriberType.User, userId)
        : null;
}
