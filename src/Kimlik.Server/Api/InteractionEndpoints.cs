using Kimlik.Application.Interactions;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Hosting;
using Kimlik.Server.Interactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Kimlik.Server.Api;

/// <summary>Login challenges, which a login app answers when sign-ins are handed to it (design §10.12).</summary>
internal static class InteractionEndpoints
{
    public static IEndpointRouteBuilder MapInteractionEndpoints(this IEndpointRouteBuilder api)
    {
        var login = api.MapGroup("interactions/login/{challenge}").WithTags("Interactions");

        login.MapGet(string.Empty, GetAsync).WithName("GetLoginChallenge").WithSummary("Read what a login challenge asks for")
            .WithDescription("The challenge is the `login_challenge` the login app was sent; unknown, expired and decided ones are not found.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.InteractionsWrite);

        login.MapPost("accept", AcceptAsync).WithName("AcceptLoginChallenge").WithSummary("Sign the person in")
            .WithDescription("Accepts the challenge for the user, signed in with the given methods, which must meet the factors the request and Kimlik's policies need. Send the browser to `redirectTo`.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.InteractionsWrite);

        login.MapPost("reject", RejectAsync).WithName("RejectLoginChallenge").WithSummary("Refuse the sign-in")
            .WithDescription("The app gets an `access_denied` error. Send the browser to `redirectTo`.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.InteractionsWrite);

        login.MapPost("password", VerifyPasswordAsync).WithName("VerifyLoginPassword").WithSummary("Check a person's password")
            .WithDescription("Checks the password Kimlik keeps for the address, counting toward the account's lockout; each challenge allows ten checks. Unknown addresses, wrong passwords and locked accounts get the same answer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.InteractionsWrite);

        login.MapPost("second-factor", VerifySecondFactorAsync).WithName("VerifyLoginSecondFactor").WithSummary("Check a person's second factor")
            .WithDescription("Checks a code from the user's authenticator app, which works once, or one of their recovery codes, which is used up.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(SystemPermissions.InteractionsWrite);

        return api;
    }

    /// <summary>Where the browser finishes a decided challenge, on Kimlik.</summary>
    internal static string CompletionUrl(ServerOptions server, string challenge) =>
        $"{server.PublicUrl!.AbsoluteUri.TrimEnd('/')}{LoginApps.CompletionPath}/{Uri.EscapeDataString(challenge)}";

    private static async Task<Results<Ok<LoginChallengeResponse>, ProblemHttpResult>> GetAsync(
        string challenge, GetLoginChallengeHandler handler, IOptions<ServerOptions> server, CancellationToken cancellationToken)
    {
        var found = await handler.HandleAsync(challenge, cancellationToken);
        return found.IsSuccess
            ? TypedResults.Ok(found.Value with { HostedSignInUrl = $"{server.Value.PublicUrl!.AbsoluteUri.TrimEnd('/')}{found.Value.HostedSignInUrl}" })
            : ApiResults.Problem(found.Error);
    }

    private static async Task<Results<Ok<LoginChallengeRedirect>, ProblemHttpResult>> AcceptAsync(
        string challenge, AcceptLoginChallengeRequest request, AcceptLoginChallengeHandler handler, IOptions<ServerOptions> server, CancellationToken cancellationToken)
    {
        var accepted = await handler.HandleAsync(challenge, request, cancellationToken);
        return accepted.IsSuccess ? TypedResults.Ok(new LoginChallengeRedirect(CompletionUrl(server.Value, challenge))) : ApiResults.Problem(accepted.Error);
    }

    private static async Task<Results<Ok<LoginChallengeRedirect>, ProblemHttpResult>> RejectAsync(
        string challenge, RejectLoginChallengeRequest request, RejectLoginChallengeHandler handler, IOptions<ServerOptions> server, CancellationToken cancellationToken)
    {
        var rejected = await handler.HandleAsync(challenge, request, cancellationToken);
        return rejected.IsSuccess ? TypedResults.Ok(new LoginChallengeRedirect(CompletionUrl(server.Value, challenge))) : ApiResults.Problem(rejected.Error);
    }

    private static async Task<Results<Ok<VerifiedUserResponse>, ProblemHttpResult>> VerifyPasswordAsync(
        string challenge, VerifyPasswordRequest request, LoginAppCredentials credentials, CancellationToken cancellationToken) =>
        (await credentials.VerifyPasswordAsync(challenge, request, cancellationToken)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> VerifySecondFactorAsync(
        string challenge, VerifySecondFactorRequest request, LoginAppCredentials credentials, CancellationToken cancellationToken) =>
        (await credentials.VerifySecondFactorAsync(challenge, request, cancellationToken)).ToNoContent();
}
