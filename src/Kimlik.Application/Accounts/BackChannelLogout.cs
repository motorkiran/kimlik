using Kimlik.Application.Abstractions;
using Kimlik.Application.Clients;
using Kimlik.Domain.Users;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Kimlik.Application.Accounts;

/// <summary>Posts a logout token (OpenID Connect Back-Channel Logout 1.0) to a client.</summary>
public interface IBackChannelLogoutSender
{
    /// <summary>
    /// Tells the client at <paramref name="endpoint"/> that the user's session ended, or every session of theirs when
    /// <paramref name="sessionId"/> is <see langword="null"/>.
    /// </summary>
    Task SendAsync(Uri endpoint, string clientId, Guid userId, string? sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// Remembers which clients with a back-channel logout URI each browser session signed in to, and tells them when the
/// session, or all of the user's sessions, end. The logout tokens go out from the outbox; ending sessions leaves saving
/// the changes to the caller.
/// </summary>
public sealed class BackChannelLogout(IKimlikDbContext context, IOpenIddictApplicationManager applications, IOutbox outbox, TimeProvider timeProvider)
{
    /// <summary>How long a session's record of a client is kept when the session does not end by signing out.</summary>
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    /// <summary>Records that the session signed the user in to the client, if the client wants to hear of logouts.</summary>
    public async Task RecordAsync(string sessionId, Guid userId, string clientId, CancellationToken cancellationToken)
    {
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application
            || ClientPresets.BackChannelLogoutUriOf(await applications.GetPropertiesAsync(application, cancellationToken)) is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (await context.SessionClients.FindAsync([sessionId, clientId], cancellationToken) is { } existing)
        {
            existing.SignInAgain(now);
        }
        else
        {
            context.SessionClients.Add(new SessionClient(sessionId, userId, clientId, now));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Tells the clients the session signed in to that it ended.</summary>
    public async Task EndSessionAsync(Guid userId, string sessionId, CancellationToken cancellationToken)
    {
        var clients = await context.SessionClients.Where(record => record.SessionId == sessionId && record.UserId == userId).ToListAsync(cancellationToken);
        foreach (var client in clients)
        {
            outbox.Enqueue(new SendBackChannelLogout(client.ClientId, userId, sessionId));
        }

        context.SessionClients.RemoveRange(clients);
    }

    /// <summary>Tells every client any of the user's sessions signed in to that all of them ended.</summary>
    public async Task EndAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var records = await context.SessionClients.Where(record => record.UserId == userId).ToListAsync(cancellationToken);
        foreach (var clientId in records.Select(record => record.ClientId).Distinct(StringComparer.Ordinal))
        {
            outbox.Enqueue(new SendBackChannelLogout(clientId, userId, SessionId: null));
        }

        context.SessionClients.RemoveRange(records);
    }
}

/// <summary>A logout token to send to a client; it is created as it goes out.</summary>
[OutboxMessage("oidc.back_channel_logout")]
public sealed record SendBackChannelLogout(string ClientId, Guid UserId, string? SessionId);

public sealed class SendBackChannelLogoutHandler(IOpenIddictApplicationManager applications, IBackChannelLogoutSender sender)
    : IOutboxMessageHandler<SendBackChannelLogout>
{
    public async Task HandleAsync(SendBackChannelLogout message, CancellationToken cancellationToken)
    {
        // The client was deleted, or no longer wants to hear of logouts, while the message was waiting.
        if (await applications.FindByClientIdAsync(message.ClientId, cancellationToken) is not { } application
            || ClientPresets.BackChannelLogoutUriOf(await applications.GetPropertiesAsync(application, cancellationToken)) is not { } endpoint)
        {
            return;
        }

        await sender.SendAsync(endpoint, message.ClientId, message.UserId, message.SessionId, cancellationToken);
    }
}
