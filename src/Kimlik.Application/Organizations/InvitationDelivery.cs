using Kimlik.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

/// <summary>
/// Sends an invitation email. Only the intent is stored in the outbox; the token is generated at send time and
/// only its hash is kept, so no usable link ever sits in the database.
/// </summary>
[OutboxMessage("organization.invitation")]
public sealed record SendInvitation(Guid InvitationId);

public sealed class SendInvitationHandler(
    IKimlikDbContext context,
    IAccountLinks links,
    IEmailTemplateRenderer renderer,
    IEmailSender sender,
    TimeProvider timeProvider) : IOutboxMessageHandler<SendInvitation>
{
    public async Task HandleAsync(SendInvitation message, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var invitation = await context.Invitations.SingleOrDefaultAsync(invitation => invitation.Id == message.InvitationId, cancellationToken);
        if (invitation is null || !invitation.IsOpenAt(now))
        {
            // Accepted, revoked or expired while the message was waiting.
            return;
        }

        var organization = await context.Organizations.AsNoTracking().SingleAsync(organization => organization.Id == invitation.OrganizationId, cancellationToken);

        // People who already have an account read it in their language.
        var locale = await context.Users
            .Where(user => user.NormalizedEmail == invitation.NormalizedEmail)
            .Select(user => user.Locale)
            .FirstOrDefaultAsync(cancellationToken);

        var token = InvitationTokens.Generate();
        invitation.IssueToken(InvitationTokens.Hash(token), now);
        await context.SaveChangesAsync(cancellationToken);

        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["organization"] = organization.Name,
            ["email"] = invitation.Email,
            ["link"] = links.Invitation(token).AbsoluteUri,
            ["days"] = Math.Max(1, (int)Math.Round((invitation.ExpiresAt - now).TotalDays)),
        };

        var email = await renderer.RenderAsync("invitation", locale, model, cancellationToken);
        await sender.SendAsync(new EmailMessage(invitation.Email, email.Subject, email.HtmlBody, email.TextBody), cancellationToken);
    }
}
