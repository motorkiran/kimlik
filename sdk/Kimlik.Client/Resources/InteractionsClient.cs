using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>
/// Login challenges, for a login app that signs people in for Kimlik with pages of its own. Requires
/// <c>kimlik.interactions:write</c>.
/// </summary>
public sealed class InteractionsClient
{
    private readonly KimlikHttp _http;

    internal InteractionsClient(KimlikHttp http) => _http = http;

    /// <summary>What the challenge, the <c>login_challenge</c> the login app was sent, asks for.</summary>
    public Task<LoginChallengeResponse> GetLoginChallengeAsync(string challenge, CancellationToken cancellationToken = default) =>
        _http.GetAsync<LoginChallengeResponse>(Path(challenge), cancellationToken);

    /// <summary>Signs the person in; send the browser to the returned URL.</summary>
    public Task<LoginChallengeRedirect> AcceptLoginChallengeAsync(string challenge, AcceptLoginChallengeRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<LoginChallengeRedirect>(HttpMethod.Post, $"{Path(challenge)}/accept", request, cancellationToken);

    /// <summary>Refuses the sign-in; send the browser to the returned URL.</summary>
    public Task<LoginChallengeRedirect> RejectLoginChallengeAsync(string challenge, RejectLoginChallengeRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<LoginChallengeRedirect>(HttpMethod.Post, $"{Path(challenge)}/reject", request, cancellationToken);

    /// <summary>Checks the password Kimlik keeps for the address.</summary>
    public Task<VerifiedUserResponse> VerifyPasswordAsync(string challenge, VerifyPasswordRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<VerifiedUserResponse>(HttpMethod.Post, $"{Path(challenge)}/password", request, cancellationToken);

    /// <summary>Checks a code from the user's authenticator app, or one of their recovery codes.</summary>
    public Task VerifySecondFactorAsync(string challenge, VerifySecondFactorRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"{Path(challenge)}/second-factor", request, cancellationToken);

    private static string Path(string challenge) => $"interactions/login/{Uri.EscapeDataString(challenge)}";
}
