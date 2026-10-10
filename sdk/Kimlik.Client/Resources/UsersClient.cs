using Kimlik.Contracts.Account;
using Kimlik.Contracts.Management;

namespace Kimlik.Client.Resources;

/// <summary>The users of the installation. Requires <c>kimlik.users:read</c>, or <c>kimlik.users:write</c> to change them.</summary>
public sealed class UsersClient
{
    private readonly KimlikHttp _http;

    internal UsersClient(KimlikHttp http) => _http = http;

    /// <param name="search">Part of the email address or name.</param>
    /// <param name="status">Only users with this status.</param>
    /// <param name="cursor">The <see cref="Page{T}.NextCursor"/> of the previous page.</param>
    /// <param name="limit">The page size, 50 by default and at most 200.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<Page<UserResponse>> ListAsync(
        string? search = null, UserStatus? status = null, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        _http.GetAsync<Page<UserResponse>>(KimlikHttp.WithQuery("users", ("q", search), ("status", status), ("cursor", cursor), ("limit", limit)), cancellationToken);

    public Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<UserResponse>($"users/{id}", cancellationToken);

    public Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<UserResponse>(HttpMethod.Post, "users", request, cancellationToken);

    /// <summary>Changes the properties <paramref name="request"/> sets; the others keep their value.</summary>
    public Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        _http.SendAsync<UserResponse>(HttpMethod.Patch, $"users/{id}", request, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}", body: null, cancellationToken);

    /// <summary>Blocks sign-in and revokes every session and token of the user.</summary>
    public Task SuspendAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"users/{id}/suspend", body: null, cancellationToken);

    public Task ReactivateAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"users/{id}/reactivate", body: null, cancellationToken);

    public Task VerifyEmailAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"users/{id}/verify-email", body: null, cancellationToken);

    public Task SendPasswordResetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"users/{id}/send-password-reset", body: null, cancellationToken);

    /// <summary>Removes the user's second factor, for someone who lost their authenticator; their sessions end.</summary>
    public Task ResetMfaAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Post, $"users/{id}/mfa/reset", body: null, cancellationToken);

    /// <summary>The user's accounts at other providers, such as Google, that they sign in with.</summary>
    public Task<IReadOnlyList<UserLoginResponse>> ListLoginsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<IReadOnlyList<UserLoginResponse>>($"users/{id}/logins", cancellationToken);

    /// <summary>Disconnects the user's account at <paramref name="provider"/>, such as <c>google</c>.</summary>
    public Task UnlinkLoginAsync(Guid id, string provider, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}/logins/{Uri.EscapeDataString(provider)}", body: null, cancellationToken);

    /// <summary>What Kimlik holds about the user, for a request under KVKK or GDPR; the export is audited.</summary>
    public Task<PersonalDataExport> ExportAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<PersonalDataExport>($"users/{id}/export", cancellationToken);

    /// <summary>The passkeys the user signs in with.</summary>
    public Task<IReadOnlyList<PasskeyResponse>> ListPasskeysAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<IReadOnlyList<PasskeyResponse>>($"users/{id}/passkeys", cancellationToken);

    /// <summary>Removes one of the user's passkeys, such as one on a device that was stolen.</summary>
    public Task RemovePasskeyAsync(Guid id, string passkeyId, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}/passkeys/{Uri.EscapeDataString(passkeyId)}", body: null, cancellationToken);

    /// <summary>Removes the user's phone number, such as one they lost; signing in with texted codes stops.</summary>
    public Task RemovePhoneNumberAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}/phone-number", body: null, cancellationToken);

    /// <summary>The applications the user is signed in to, newest first.</summary>
    public Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.GetAsync<IReadOnlyList<SessionResponse>>($"users/{id}/sessions", cancellationToken);

    /// <summary>Signs one application out for the user: its tokens stop working.</summary>
    public Task RevokeSessionAsync(Guid id, Guid sessionId, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}/sessions/{sessionId}", body: null, cancellationToken);

    /// <summary>Signs the user out everywhere: every application session and token ends.</summary>
    public Task RevokeSessionsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _http.SendAsync(HttpMethod.Delete, $"users/{id}/sessions", body: null, cancellationToken);

    /// <summary>Replaces the user's global roles.</summary>
    public Task<UserResponse> SetRolesAsync(Guid id, IReadOnlyList<string> roles, CancellationToken cancellationToken = default) =>
        _http.SendAsync<UserResponse>(HttpMethod.Put, $"users/{id}/roles", new SetRolesRequest { Roles = roles }, cancellationToken);
}
