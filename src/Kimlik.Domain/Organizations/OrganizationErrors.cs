using Kimlik.Domain.Common;

namespace Kimlik.Domain.Organizations;

public static class OrganizationErrors
{
    public static readonly Error NotFound = Error.NotFound("organization.not_found", "The organization does not exist.");

    public static readonly Error SlugTaken = Error.Conflict("organization.slug_taken", "Another organization has this slug.");

    public static readonly Error MemberNotFound = Error.NotFound("organization.member_not_found", "The user is not a member of the organization.");

    public static readonly Error AlreadyMember = Error.Conflict("organization.already_member", "The user is already a member of the organization.");

    public static readonly Error InvalidName = Error.Validation("organization.invalid_name", "A name is required and is at most 100 characters.");

    public static readonly Error InvalidSlug = Error.Validation(
        "organization.invalid_slug", "A slug is up to 64 lowercase letters, digits and inner hyphens, such as 'acme' or 'acme-labs'.");

    public static readonly Error GlobalRoleNotAssignable = Error.Validation(
        "organization.global_role_not_assignable", "Members hold organization roles; global roles are assigned to users directly.");
}
