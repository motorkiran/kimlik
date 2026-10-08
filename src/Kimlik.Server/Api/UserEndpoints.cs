using System.ComponentModel;
using Kimlik.Application.Common;
using Kimlik.Application.Mfa;
using Kimlik.Application.Users;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimlik.Server.Api;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("users").WithTags("Users");

        users.MapGet(string.Empty, ListAsync)
            .WithName("ListUsers")
            .WithSummary("List users")
            .WithDescription("Lists users in creation order. `q` matches part of the email address or name.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(SystemPermissions.UsersRead);

        users.MapPost(string.Empty, CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Without a password, the user sets one through a password reset.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapGet("{id:guid}", GetAsync)
            .WithName("GetUser")
            .WithSummary("Get a user")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersRead);

        users.MapPatch("{id:guid}", UpdateAsync)
            .WithName("UpdateUser")
            .WithSummary("Update a user's profile")
            .WithDescription("JSON Merge Patch: omitted properties keep their value and `null` clears one.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapDelete("{id:guid}", DeleteAsync)
            .WithName("DeleteUser")
            .WithSummary("Delete a user")
            .WithDescription("Deletes the account and its personal data, and revokes its sessions and tokens.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPost("{id:guid}/suspend", SuspendAsync)
            .WithName("SuspendUser")
            .WithSummary("Suspend a user")
            .WithDescription("Blocks sign-in and revokes every session and token of the user at once.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPost("{id:guid}/reactivate", ReactivateAsync)
            .WithName("ReactivateUser")
            .WithSummary("Reactivate a suspended user")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPost("{id:guid}/verify-email", VerifyEmailAsync)
            .WithName("VerifyUserEmail")
            .WithSummary("Mark a user's email address as verified")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPost("{id:guid}/send-password-reset", SendPasswordResetAsync)
            .WithName("SendUserPasswordReset")
            .WithSummary("Email a user a password reset link")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPost("{id:guid}/mfa/reset", ResetMfaAsync)
            .WithName("ResetUserMfa")
            .WithSummary("Remove a user's second factor")
            .WithDescription("For a user who lost their authenticator. Their sessions end, and they set up a second factor again at their next sign-in if the policy requires one.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        users.MapPut("{id:guid}/roles", SetRolesAsync)
            .WithName("SetUserRoles")
            .WithSummary("Replace a user's roles")
            .WithDescription("Granting system permissions requires holding them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.UsersWrite);

        return api;
    }

    private static async Task<Results<Ok<Page<UserResponse>>, ProblemHttpResult>> ListAsync(
        [Description("Part of the email address or name.")] string? q,
        [Description("`active` or `suspended`.")] string? status,
        [Description("The `nextCursor` of the previous page.")] string? cursor,
        [Description("Page size, 50 by default and at most 200.")] int? limit,
        ListUsersHandler handler,
        CancellationToken cancellationToken)
    {
        if (!QueryValues.TryParseEnum<UserStatus>(status, out var statusFilter))
        {
            return ApiResults.Problem(CommonErrors.InvalidParameter(nameof(status)));
        }

        return (await handler.HandleAsync(new ListUsersQuery(q, statusFilter, cursor, limit), cancellationToken)).ToOk();
    }

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request, CreateUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToCreated(user => $"{ManagementApi.BasePath}/users/{user.Id}");

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetAsync(
        Guid id, GetUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToOk();

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateUserRequest request, UpdateUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, DeleteUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> SuspendAsync(
        Guid id, SuspendUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> ReactivateAsync(
        Guid id, ReactivateUserHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> VerifyEmailAsync(
        Guid id, VerifyUserEmailHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> SendPasswordResetAsync(
        Guid id, SendPasswordResetHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> SetRolesAsync(
        Guid id, SetRolesRequest request, SetUserRolesHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetMfaAsync(
        Guid id, ResetUserMfaHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToNoContent();
}
