using Kimlik.Domain.Access;
using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
}

/// <summary>
/// An invitation to join an organization, sent to an email address. Its link carries a random token, of which
/// only a hash is stored; sending the invitation again replaces the token.
/// </summary>
public sealed class Invitation
{
    public const int EmailMaxLength = 256;

    private readonly List<InvitationRole> _roles = [];

    // Used by EF Core.
    private Invitation()
    {
    }

    public Guid Id { get; private init; }

    public Guid OrganizationId { get; private init; }

    public string Email { get; private init; } = string.Empty;

    /// <summary>The address as ASP.NET Core Identity normalizes it, to match it with an account.</summary>
    public string NormalizedEmail { get; private init; } = string.Empty;

    /// <summary>The SHA-256 hash of the token in the most recent invitation email; none before the first one is sent.</summary>
    public string? TokenHash { get; private set; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public IReadOnlyCollection<InvitationRole> Roles => _roles;

    public static Result<Invitation> Create(
        Guid organizationId, string email, string normalizedEmail, IEnumerable<Role> roles, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var wanted = roles.ToList();
        if (!wanted.TrueForAll(role => role.Scope == RoleScope.Organization))
        {
            return OrganizationErrors.GlobalRoleNotAssignable;
        }

        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = organizationId,
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            Status = InvitationStatus.Pending,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        invitation._roles.AddRange(wanted.Select(role => new InvitationRole(invitation.Id, role.Id)));
        return invitation;
    }

    public bool IsOpenAt(DateTimeOffset now) => Status == InvitationStatus.Pending && now < ExpiresAt;

    /// <summary>Records the token of a new invitation email, which invalidates the link of any earlier one.</summary>
    public void IssueToken(string tokenHash, DateTimeOffset now)
    {
        TokenHash = tokenHash;
        UpdatedAt = now;
    }

    /// <summary>Gives a pending invitation a new expiry, to send it again.</summary>
    public Result Renew(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
        {
            return OrganizationErrors.InvitationClosed;
        }

        ExpiresAt = expiresAt;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Accept(Guid userId, DateTimeOffset now)
    {
        if (!IsOpenAt(now))
        {
            return OrganizationErrors.InvitationClosed;
        }

        Status = InvitationStatus.Accepted;
        AcceptedByUserId = userId;
        TokenHash = null;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
        {
            return OrganizationErrors.InvitationClosed;
        }

        Status = InvitationStatus.Revoked;
        TokenHash = null;
        UpdatedAt = now;
        return Result.Success();
    }
}

/// <summary>An organization role the invited user gets on joining.</summary>
public sealed class InvitationRole(Guid invitationId, Guid roleId)
{
    public Guid InvitationId { get; private init; } = invitationId;

    public Guid RoleId { get; private init; } = roleId;
}
