using System.ComponentModel;
using System.Security.Claims;
using Kimlik.Application.Accounts;
using Kimlik.Application.ApiKeys;
using Kimlik.Application.Mfa;
using Kimlik.Application.Organizations;
using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

/// <summary>The Account API: the signed-in user's own account, organizations and invitations.</summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var me = api.MapGroup("me").WithTags("Account").RequireSignedInUser();

        me.MapGet(string.Empty, GetAccountAsync).WithName("GetMyAccount").WithSummary("Get the signed-in user");

        me.MapPatch(string.Empty, UpdateProfileAsync).WithName("UpdateMyProfile")
            .WithSummary("Update my profile")
            .WithDescription("JSON Merge Patch: omitted properties keep their value and `null` clears one.")
            .ProducesValidationProblem();

        me.MapPost("password", ChangePasswordAsync).WithName("ChangeMyPassword")
            .WithSummary("Change my password")
            .WithDescription("Every session and token of the account ends, this one included.")
            .ProducesValidationProblem();

        me.MapGet("sessions", ListSessionsAsync).WithName("ListMySessions")
            .WithSummary("List the applications I am signed in to");

        me.MapDelete("sessions/{id:guid}", RevokeSessionAsync).WithName("RevokeMySession")
            .WithSummary("Sign an application out")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("sessions", RevokeAllSessionsAsync).WithName("RevokeAllMySessions")
            .WithSummary("Sign out everywhere")
            .WithDescription("Every application session and token ends, this one included; browser sessions end within minutes.");

        me.MapGet("logins", ListLoginsAsync).WithName("ListMyLogins")
            .WithSummary("List the accounts at other providers that I sign in with");

        me.MapDelete("logins/{provider}", UnlinkLoginAsync).WithName("UnlinkMyLogin")
            .WithSummary("Disconnect an account at another provider")
            .WithDescription("Not possible for the last way to sign in: set a password or connect another account first.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("api-keys", ListApiKeysAsync).WithName("ListMyApiKeys")
            .WithSummary("List my API keys")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        me.MapPost("api-keys", CreateApiKeyAsync).WithName("CreateMyApiKey")
            .WithSummary("Create an API key")
            .WithDescription("The key acts for me, with permissions I hold through my global roles; it loses those I lose. The secret is returned only this once.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapDelete("api-keys/{id:guid}", RevokeApiKeyAsync).WithName("RevokeMyApiKey")
            .WithSummary("Revoke one of my API keys")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("delete", DeleteAccountAsync).WithName("DeleteMyAccount")
            .WithSummary("Delete my account")
            .WithDescription("Takes the password. The account and its personal data are deleted, and every session ends.")
            .ProducesValidationProblem();

        me.MapGet("organizations", ListOrganizationsAsync).WithName("ListMyOrganizations")
            .WithSummary("List my organizations")
            .WithDescription("The organizations the user belongs to, with the user's roles and permissions in each.");

        me.MapPost("organizations", CreateOrganizationAsync).WithName("CreateMyOrganization")
            .WithSummary("Create an organization")
            .WithDescription("The user becomes its first member, with the creator roles of the installation.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPatch("organizations/{id:guid}", UpdateOrganizationAsync).WithName("UpdateMyOrganization")
            .WithSummary("Rename an organization or change its slug")
            .WithDescription("Requires `kimlik.org.settings:write` in the organization.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}", DeleteOrganizationAsync).WithName("DeleteMyOrganization")
            .WithSummary("Delete an organization")
            .WithDescription("Requires `kimlik.org.settings:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/membership", LeaveOrganizationAsync).WithName("LeaveOrganization")
            .WithSummary("Leave an organization")
            .WithDescription("Someone else must be able to manage the members afterwards, unless nobody else is left.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("organizations/{id:guid}/members", ListMembersAsync).WithName("ListMyOrganizationMembers")
            .WithSummary("List the members of an organization")
            .WithDescription("Requires `kimlik.org.members:read` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("organizations/{id:guid}/members/{userId:guid}/roles", SetMemberRolesAsync).WithName("SetMyOrganizationMemberRoles")
            .WithSummary("Replace a member's roles")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission involved.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/members/{userId:guid}", RemoveMemberAsync).WithName("RemoveMyOrganizationMember")
            .WithSummary("Remove a member")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission the member holds.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("organizations/{id:guid}/api-keys", ListOrganizationApiKeysAsync).WithName("ListMyOrganizationApiKeys")
            .WithSummary("List the API keys of an organization")
            .WithDescription("Requires `kimlik.org.api_keys:read` in the organization.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("organizations/{id:guid}/api-keys", CreateOrganizationApiKeyAsync).WithName("CreateMyOrganizationApiKey")
            .WithSummary("Create an API key for an organization")
            .WithDescription("Requires `kimlik.org.api_keys:write` in the organization. The key acts for the organization, with permissions "
                + "I hold in it, and keeps them when I leave. The secret is returned only this once.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapDelete("organizations/{id:guid}/api-keys/{keyId:guid}", RevokeOrganizationApiKeyAsync).WithName("RevokeMyOrganizationApiKey")
            .WithSummary("Revoke an API key of an organization")
            .WithDescription("Requires `kimlik.org.api_keys:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("organizations/{id:guid}/invitations", ListInvitationsAsync).WithName("ListMyOrganizationInvitations")
            .WithSummary("List the invitations of an organization")
            .WithDescription("Requires `kimlik.org.members:read` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("organizations/{id:guid}/invitations", InviteAsync).WithName("CreateMyOrganizationInvitation")
            .WithSummary("Invite someone to an organization")
            .WithDescription("Requires `kimlik.org.members:write` in the organization, and every organization permission the roles carry.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("organizations/{id:guid}/invitations/{invitationId:guid}/resend", ResendInvitationAsync).WithName("ResendMyOrganizationInvitation")
            .WithSummary("Send an invitation again")
            .WithDescription("Requires `kimlik.org.members:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("organizations/{id:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync).WithName("RevokeMyOrganizationInvitation")
            .WithSummary("Revoke an invitation")
            .WithDescription("Requires `kimlik.org.members:write` in the organization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("mfa", GetMfaAsync).WithName("GetMyMfa").WithSummary("Get my two-factor authentication status")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("mfa/authenticator", BeginAuthenticatorSetupAsync).WithName("SetUpMyAuthenticator")
            .WithSummary("Set up an authenticator app")
            .WithDescription("Returns a new key for an authenticator app. Two-factor authentication turns on once a code from the app confirms it.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("mfa/authenticator/confirm", ConfirmAuthenticatorAsync).WithName("ConfirmMyAuthenticator")
            .WithSummary("Confirm the authenticator app and turn two-factor authentication on")
            .WithDescription("Returns the recovery codes, which are shown only this once.")
            .ProducesValidationProblem();

        me.MapPost("mfa/recovery-codes", RegenerateRecoveryCodesAsync).WithName("RegenerateMyRecoveryCodes")
            .WithSummary("Replace my recovery codes")
            .WithDescription("Takes a current code from the authenticator app. The old codes stop working.")
            .ProducesValidationProblem();

        me.MapPost("mfa/disable", DisableMfaAsync).WithName("DisableMyMfa")
            .WithSummary("Turn two-factor authentication off")
            .WithDescription("Takes a current code from the authenticator app. Not possible when the policy requires it for the account.")
            .ProducesValidationProblem();

        me.MapGet("invitations", ListInvitationsToMeAsync).WithName("ListMyInvitations")
            .WithSummary("List invitations to me")
            .WithDescription("Pending invitations to the user's verified email address.");

        me.MapPost("invitations/{id:guid}/accept", AcceptInvitationAsync).WithName("AcceptMyInvitation")
            .WithSummary("Accept an invitation")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("invitations/{id:guid}/decline", DeclineInvitationAsync).WithName("DeclineMyInvitation")
            .WithSummary("Decline an invitation")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    private static Guid Caller(ClaimsPrincipal principal) => AccountCaller.UserId(principal)!.Value;

    private static async Task<Results<Ok<ProfileResponse>, ProblemHttpResult>> GetAccountAsync(
        ClaimsPrincipal principal, MyAccount account, CancellationToken cancellationToken) =>
        (await account.GetProfileAsync(Caller(principal), cancellationToken)).ToOk();

    private static async Task<Ok<IReadOnlyList<MyOrganizationResponse>>> ListOrganizationsAsync(
        ClaimsPrincipal principal, MyOrganizations organizations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await organizations.ListAsync(Caller(principal), cancellationToken));

    private static async Task<Results<Created<MyOrganizationResponse>, ProblemHttpResult>> CreateOrganizationAsync(
        ClaimsPrincipal principal, CreateMyOrganizationRequest request, MyOrganizations organizations, CancellationToken cancellationToken) =>
        (await organizations.CreateAsync(Caller(principal), request, cancellationToken)).ToCreated(organization => $"{ManagementApi.BasePath}/me/organizations/{organization.Id}");

    private static async Task<Results<Ok<MyOrganizationResponse>, ProblemHttpResult>> UpdateOrganizationAsync(
        ClaimsPrincipal principal, Guid id, UpdateMyOrganizationRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(Caller(principal), id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteOrganizationAsync(
        ClaimsPrincipal principal, Guid id, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> LeaveOrganizationAsync(
        ClaimsPrincipal principal, Guid id, MyOrganizations organizations, CancellationToken cancellationToken) =>
        (await organizations.LeaveAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<MemberResponse>>, ProblemHttpResult>> ListMembersAsync(
        ClaimsPrincipal principal,
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        OrganizationSelfService service,
        CancellationToken cancellationToken) =>
        (await service.ListMembersAsync(Caller(principal), new ListMembersQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Ok<MemberResponse>, ProblemHttpResult>> SetMemberRolesAsync(
        ClaimsPrincipal principal, Guid id, Guid userId, SetRolesRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.SetMemberRolesAsync(Caller(principal), id, userId, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveMemberAsync(
        ClaimsPrincipal principal, Guid id, Guid userId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.RemoveMemberAsync(Caller(principal), id, userId, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<InvitationResponse>>, ProblemHttpResult>> ListInvitationsAsync(
        ClaimsPrincipal principal,
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        OrganizationSelfService service,
        CancellationToken cancellationToken) =>
        (await service.ListInvitationsAsync(Caller(principal), new ListInvitationsQuery(id, cursor, limit), cancellationToken)).ToOk();

    private static async Task<Results<Created<InvitationResponse>, ProblemHttpResult>> InviteAsync(
        ClaimsPrincipal principal, Guid id, CreateInvitationRequest request, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.InviteAsync(Caller(principal), id, request, cancellationToken))
            .ToCreated(invitation => $"{ManagementApi.BasePath}/me/organizations/{id}/invitations/{invitation.Id}");

    private static async Task<Results<Ok<InvitationResponse>, ProblemHttpResult>> ResendInvitationAsync(
        ClaimsPrincipal principal, Guid id, Guid invitationId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.ResendInvitationAsync(Caller(principal), id, invitationId, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeInvitationAsync(
        ClaimsPrincipal principal, Guid id, Guid invitationId, OrganizationSelfService service, CancellationToken cancellationToken) =>
        (await service.RevokeInvitationAsync(Caller(principal), id, invitationId, cancellationToken)).ToNoContent();

    private static async Task<Ok<IReadOnlyList<MyInvitationResponse>>> ListInvitationsToMeAsync(
        ClaimsPrincipal principal, MyInvitations invitations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await invitations.ListAsync(Caller(principal), cancellationToken));

    private static async Task<Results<NoContent, ProblemHttpResult>> AcceptInvitationAsync(
        ClaimsPrincipal principal, Guid id, MyInvitations invitations, CancellationToken cancellationToken)
    {
        var accepted = await invitations.AcceptAsync(Caller(principal), id, cancellationToken);
        return accepted.IsSuccess ? TypedResults.NoContent() : ApiResults.Problem(accepted.Error);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeclineInvitationAsync(
        ClaimsPrincipal principal, Guid id, MyInvitations invitations, CancellationToken cancellationToken) =>
        (await invitations.DeclineAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<MfaStatusResponse>, ProblemHttpResult>> GetMfaAsync(
        ClaimsPrincipal principal, TwoFactor twoFactor, CancellationToken cancellationToken) =>
        (await twoFactor.StatusAsync(Caller(principal), cancellationToken)).ToOk();

    private static async Task<Results<Ok<AuthenticatorSetupResponse>, ProblemHttpResult>> BeginAuthenticatorSetupAsync(
        ClaimsPrincipal principal, TwoFactor twoFactor) =>
        (await twoFactor.BeginSetupAsync(Caller(principal))).ToOk();

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> ConfirmAuthenticatorAsync(
        ClaimsPrincipal principal, AuthenticatorCodeRequest request, TwoFactor twoFactor, CancellationToken cancellationToken) =>
        (await twoFactor.ConfirmSetupAsync(Caller(principal), request.Code, cancellationToken)).ToOk();

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> RegenerateRecoveryCodesAsync(
        ClaimsPrincipal principal, AuthenticatorCodeRequest request, TwoFactor twoFactor, CancellationToken cancellationToken) =>
        (await twoFactor.RegenerateRecoveryCodesAsync(Caller(principal), request.Code, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DisableMfaAsync(
        ClaimsPrincipal principal, AuthenticatorCodeRequest request, TwoFactor twoFactor, CancellationToken cancellationToken) =>
        (await twoFactor.DisableAsync(Caller(principal), request.Code, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<ProfileResponse>, ProblemHttpResult>> UpdateProfileAsync(
        ClaimsPrincipal principal, UpdateProfileRequest request, MyAccount account, CancellationToken cancellationToken) =>
        (await account.UpdateProfileAsync(Caller(principal), request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ClaimsPrincipal principal, ChangePasswordRequest request, MyAccount account, CancellationToken cancellationToken) =>
        (await account.ChangePasswordAsync(Caller(principal), request, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<IReadOnlyList<SessionResponse>>, ProblemHttpResult>> ListSessionsAsync(
        ClaimsPrincipal principal, ApplicationSessions sessions, CancellationToken cancellationToken) =>
        (await sessions.ListAsync(Caller(principal), cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeSessionAsync(
        ClaimsPrincipal principal, Guid id, ApplicationSessions sessions, CancellationToken cancellationToken) =>
        (await sessions.RevokeAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAllSessionsAsync(
        ClaimsPrincipal principal, ApplicationSessions sessions, CancellationToken cancellationToken) =>
        (await sessions.RevokeAllAsync(Caller(principal), cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<IReadOnlyList<UserLoginResponse>>, ProblemHttpResult>> ListLoginsAsync(
        ClaimsPrincipal principal, ExternalLogins logins) =>
        (await logins.ListAsync(Caller(principal))).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> UnlinkLoginAsync(
        ClaimsPrincipal principal, string provider, ExternalLogins logins, CancellationToken cancellationToken) =>
        (await logins.UnlinkAsync(Caller(principal), provider, keepASignInMethod: true, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<ApiKeyResponse>>, ProblemHttpResult>> ListApiKeysAsync(
        ClaimsPrincipal principal,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        MyApiKeys apiKeys,
        CancellationToken cancellationToken) =>
        (await apiKeys.ListAsync(Caller(principal), cursor, limit, cancellationToken)).ToOk();

    private static async Task<Results<Created<CreatedApiKeyResponse>, ProblemHttpResult>> CreateApiKeyAsync(
        ClaimsPrincipal principal, CreateApiKeyRequest request, MyApiKeys apiKeys, CancellationToken cancellationToken) =>
        (await apiKeys.CreateAsync(Caller(principal), request, cancellationToken)).ToCreated(created => $"/api/v1/me/api-keys/{created.ApiKey.Id}");

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeApiKeyAsync(
        ClaimsPrincipal principal, Guid id, MyApiKeys apiKeys, CancellationToken cancellationToken) =>
        (await apiKeys.RevokeAsync(Caller(principal), id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<Page<ApiKeyResponse>>, ProblemHttpResult>> ListOrganizationApiKeysAsync(
        ClaimsPrincipal principal,
        Guid id,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        MyApiKeys apiKeys,
        CancellationToken cancellationToken) =>
        (await apiKeys.ListForOrganizationAsync(Caller(principal), id, cursor, limit, cancellationToken)).ToOk();

    private static async Task<Results<Created<CreatedApiKeyResponse>, ProblemHttpResult>> CreateOrganizationApiKeyAsync(
        ClaimsPrincipal principal, Guid id, CreateApiKeyRequest request, MyApiKeys apiKeys, CancellationToken cancellationToken) =>
        (await apiKeys.CreateForOrganizationAsync(Caller(principal), id, request, cancellationToken))
            .ToCreated(created => $"/api/v1/me/organizations/{id}/api-keys/{created.ApiKey.Id}");

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeOrganizationApiKeyAsync(
        ClaimsPrincipal principal, Guid id, Guid keyId, MyApiKeys apiKeys, CancellationToken cancellationToken) =>
        (await apiKeys.RevokeForOrganizationAsync(Caller(principal), id, keyId, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAccountAsync(
        ClaimsPrincipal principal, DeleteAccountRequest request, MyAccount account, CancellationToken cancellationToken) =>
        (await account.DeleteAsync(Caller(principal), request, cancellationToken)).ToNoContent();
}
