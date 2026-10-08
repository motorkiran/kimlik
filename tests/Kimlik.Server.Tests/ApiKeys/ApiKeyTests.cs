using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Api;
using Kimlik.Server.Tests.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.ApiKeys;

/// <summary>API keys created by their owners through the Account API, and verified by resource servers.</summary>
public sealed class ApiKeyTests(KimlikServerFixture server)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserKey_ActsForTheUser_WithThePermissionsTheyStillHold()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        using var verifier = await VerifierAsync();

        using var response = await me.PostJsonAsync("/api/v1/me/api-keys", new CreateApiKeyRequest { Name = "Reporting", Permissions = [permissions[0]] });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadAsync<CreatedApiKeyResponse>();
        created.Key.ShouldStartWith("kmk_");
        created.Key.Length.ShouldBe(47);
        created.ApiKey.Prefix.ShouldBe(created.Key[..12]);
        created.ApiKey.UserId.ShouldBe(user.Id);

        var verified = await VerifyAsync(verifier, created.Key);
        verified.ShouldBe(new ApiKeyVerificationResponse(true, created.ApiKey.Id, user.Id, null, verified.Permissions, null, null));
        verified.Permissions.ShouldBe([permissions[0]]);

        // The key acts for the user: what they lose, it loses; while they are suspended, it does not work.
        await server.QueryDatabaseAsync(context => context.UserRoles.Where(assignment => assignment.UserId == user.Id).ExecuteDeleteAsync(CancellationToken));
        (await VerifyAsync(verifier, created.Key)).Permissions.ShouldBeEmpty();

        using var admin = await server.CreateApiClientAsync();
        using var suspended = await admin.Http.PostAsync($"/api/v1/users/{user.Id}/suspend");
        (await VerifyAsync(verifier, created.Key)).Active.ShouldBeFalse();
    }

    [Fact]
    public async Task Keys_CannotHoldMoreThanTheirCreator_NorKimliksOwnPermissions()
    {
        var (role, permissions) = await server.CreateRoleAsync();
        var (_, otherPermissions) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();
        await server.AssignToUserAsync(user.Id, role);
        await server.AssignToUserAsync(user.Id, SystemRoles.Admin);
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));

        (await CreateAsync(me, otherPermissions[0])).ShouldBe("api_key.permission_not_held");
        (await CreateAsync(me, SystemPermissions.UsersRead)).ShouldBe("api_key.system_permission");
        (await CreateAsync(me, "nothing:here")).ShouldBe("access.unknown_permission");
        (await CreateAsync(me, permissions[1])).ShouldBeNull();
    }

    [Fact]
    public async Task RevokedAndExpiredKeys_StopWorking()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        using var verifier = await VerifierAsync();
        var revoked = await CreateKeyAsync(me);
        var expired = await CreateKeyAsync(me);

        using var revocation = await me.DeleteAsync($"/api/v1/me/api-keys/{revoked.ApiKey.Id}", CancellationToken);
        revocation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await server.QueryDatabaseAsync(context => context.ApiKeys.Where(key => key.Id == expired.ApiKey.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), CancellationToken));

        (await VerifyAsync(verifier, revoked.Key)).ShouldBe(new ApiKeyVerificationResponse(false));
        (await VerifyAsync(verifier, expired.Key)).Active.ShouldBeFalse();
        (await VerifyAsync(verifier, "kmk_not-a-key")).Active.ShouldBeFalse();

        using var listed = await me.GetAsync("/api/v1/me/api-keys", CancellationToken);
        (await listed.ReadAsync<Page<ApiKeyResponse>>()).Items.Single(key => key.Id == revoked.ApiKey.Id).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task OrganizationKey_ActsForTheOrganization()
    {
        var admin = await server.CreateUserAsync();
        var member = await server.CreateUserAsync();
        var organization = await server.CreateOrganizationAsync(admin.Id, member.Id);
        await server.SetMemberRolesAsync(organization.Id, admin.Id, organization.Role, SystemRoles.OrganizationAdmin);
        using var verifier = await VerifierAsync();

        using var asMember = server.WithToken(await server.UserAccessTokenAsync(member));
        using var refused = await asMember.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/api-keys", new CreateApiKeyRequest { Name = "CI", Permissions = [organization.Permission] });
        (await refused.ReadProblemCodeAsync()).ShouldBe("organization.missing_permission");

        using var asAdmin = server.WithToken(await server.UserAccessTokenAsync(admin));
        using var response = await asAdmin.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/api-keys", new CreateApiKeyRequest { Name = "CI", Permissions = [organization.Permission] });
        var created = await response.ReadAsync<CreatedApiKeyResponse>();

        var verified = await VerifyAsync(verifier, created.Key);
        verified.OrganizationId.ShouldBe(organization.Id);
        verified.UserId.ShouldBeNull();
        verified.Permissions.ShouldBe([organization.Permission]);

        // The organization's key outlives the membership of the member who created it.
        await server.RemoveMemberAsync(organization.Id, admin.Id);
        (await VerifyAsync(verifier, created.Key)).Active.ShouldBeTrue();
    }

    [Fact]
    public async Task OrganizationKey_OnlyTakesPermissionsOfOrganizationRoles()
    {
        var (globalRole, globalPermissions) = await server.CreateRoleAsync();
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, globalRole);
        var organization = await server.CreateOrganizationAsync(admin.Id);
        await server.SetMemberRolesAsync(organization.Id, admin.Id, organization.Role, SystemRoles.OrganizationAdmin);
        using var me = server.WithToken(await server.UserAccessTokenAsync(admin));

        // The key would keep it after the creator's global role is gone.
        using var refused = await me.PostJsonAsync(
            $"/api/v1/me/organizations/{organization.Id}/api-keys", new CreateApiKeyRequest { Name = "CI", Permissions = [globalPermissions[0]] });

        (await refused.ReadProblemCodeAsync()).ShouldBe("api_key.permission_not_held");
    }

    [Fact]
    public async Task ManagementApi_ListsAndRevokesKeys_AndVerificationNeedsItsPermission()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        var created = await CreateKeyAsync(me);
        using var admin = await server.CreateApiClientAsync();

        using var listed = await admin.Http.GetAsync($"/api/v1/api-keys?userId={user.Id}", CancellationToken);
        (await listed.ReadAsync<Page<ApiKeyResponse>>()).Items.ShouldHaveSingleItem().Id.ShouldBe(created.ApiKey.Id);

        using var revoked = await admin.Http.DeleteAsync($"/api/v1/api-keys/{created.ApiKey.Id}", CancellationToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stranger = await server.CreateApiClientAsync(role: null);
        using var forbidden = await stranger.Http.PostJsonAsync("/api/v1/api-keys/verify", new VerifyApiKeyRequest { Key = created.Key });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Verification_RecordsWhenTheKeyWasLastUsed()
    {
        var user = await server.CreateUserAsync();
        using var me = server.WithToken(await server.UserAccessTokenAsync(user));
        var created = await CreateKeyAsync(me);
        created.ApiKey.LastUsedAt.ShouldBeNull();
        using var verifier = await VerifierAsync();

        await VerifyAsync(verifier, created.Key);

        using var listed = await me.GetAsync("/api/v1/me/api-keys", CancellationToken);
        (await listed.ReadAsync<Page<ApiKeyResponse>>()).Items.Single().LastUsedAt.ShouldNotBeNull();
    }

    private async Task<ApiClient> VerifierAsync() => await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.ApiKeysVerify));

    private static async Task<ApiKeyVerificationResponse> VerifyAsync(ApiClient verifier, string key)
    {
        using var response = await verifier.Http.PostJsonAsync("/api/v1/api-keys/verify", new VerifyApiKeyRequest { Key = key });
        return await response.ReadAsync<ApiKeyVerificationResponse>();
    }

    private static async Task<CreatedApiKeyResponse> CreateKeyAsync(HttpClient me)
    {
        using var response = await me.PostJsonAsync("/api/v1/me/api-keys", new CreateApiKeyRequest { Name = "Key", Permissions = [] });
        return await response.ReadAsync<CreatedApiKeyResponse>();
    }

    /// <summary>The problem code of creating a key with the permission, or <see langword="null"/> when it was created.</summary>
    private static async Task<string?> CreateAsync(HttpClient me, string permission)
    {
        using var response = await me.PostJsonAsync("/api/v1/me/api-keys", new CreateApiKeyRequest { Name = "Key", Permissions = [permission] });
        return response.StatusCode == HttpStatusCode.Created ? null : await response.ReadProblemCodeAsync();
    }
}
