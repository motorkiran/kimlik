using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Domain.Auditing;
using Kimlik.Server.Tests.Access;
using Kimlik.Server.Tests.Accounts;
using Kimlik.Server.Tests.Oidc;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Server.Tests.Api;

public sealed class UserApiTests(KimlikServerFixture server)
{
    private const string Users = "/api/v1/users";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_WithoutAccessToken_IsUnauthorized()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync(Users, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task AccessToken_ForAnotherApi_IsUnauthorized()
    {
        var serviceClient = await server.CreateServiceClientAsync();
        await server.AssignToClientAsync(serviceClient.ClientId, SystemRoles.Admin);
        using var http = server.CreateClient();
        using var api = server.WithToken(await http.RequestClientCredentialsTokenAsync(serviceClient));

        using var response = await api.GetAsync(Users, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AccessToken_WithoutThePermission_IsForbidden()
    {
        using var api = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.UsersRead));

        using var read = await api.Http.GetAsync(Users, CancellationToken);
        using var write = await api.Http.PostJsonAsync(Users, new CreateUserRequest { Email = NewEmail() });

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_CanBeCreatedReadUpdatedAndDeleted()
    {
        using var api = await server.CreateApiClientAsync();
        var email = NewEmail();

        using var created = await api.Http.PostJsonAsync(Users, new CreateUserRequest
        {
            Email = email,
            GivenName = "Ada",
            FamilyName = "Lovelace",
            Locale = "en",
            EmailVerified = true,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = await created.ReadAsync<UserResponse>();
        created.Headers.Location!.OriginalString.ShouldBe($"{Users}/{user.Id}");
        user.Email.ShouldBe(email);
        user.EmailVerified.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
        user.Roles.ShouldBeEmpty();

        // JSON Merge Patch: a value changes, null clears, and what is left out stays.
        using var patched = await api.Http.SendJsonAsync(HttpMethod.Patch, $"{Users}/{user.Id}", """{ "givenName": "Augusta", "familyName": null }""");
        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await patched.ReadAsync<UserResponse>();
        updated.Name.ShouldBe("Augusta");
        updated.FamilyName.ShouldBeNull();
        updated.Locale.ShouldBe("en");

        using var fetched = await api.Http.GetAsync($"{Users}/{user.Id}", CancellationToken);
        (await fetched.ReadAsync<UserResponse>()).GivenName.ShouldBe("Augusta");

        using var deleted = await api.Http.DeleteAsync($"{Users}/{user.Id}", CancellationToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var gone = await api.Http.GetAsync($"{Users}/{user.Id}", CancellationToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.ReadProblemCodeAsync()).ShouldBe("user.not_found");
    }

    [Fact]
    public async Task CreateUser_WithTakenEmail_IsConflict()
    {
        var existing = await server.CreateUserAsync();
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync(Users, new CreateUserRequest { Email = existing.Email.ToUpperInvariant() });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemCodeAsync()).ShouldBe("account.email_already_registered");
    }

    [Fact]
    public async Task CreateUser_WithInvalidBody_ReturnsValidationProblem()
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.SendJsonAsync(HttpMethod.Post, Users, """{ "email": "not an address" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadJsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("request.invalid");
        problem.GetProperty("errors").EnumerateObject().ShouldContain(error => error.Name.Equals("email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateUser_WithShortPassword_IsRejected()
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostJsonAsync(Users, new CreateUserRequest { Email = NewEmail(), Password = "too short" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe("account.password_too_short");
    }

    [Fact]
    public async Task ListUsers_PagesThroughMatches_InCreationOrder()
    {
        using var api = await server.CreateApiClientAsync();
        var marker = NewMarker();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            using var created = await api.Http.PostJsonAsync(Users, new CreateUserRequest { Email = NewEmail(), FamilyName = marker });
            ids.Add((await created.ReadAsync<UserResponse>()).Id);
        }

        using var first = await api.Http.GetAsync($"{Users}?q={marker}&limit=2", CancellationToken);
        var firstPage = await first.ReadAsync<Page<UserResponse>>();
        firstPage.Items.Select(user => user.Id).ShouldBe(ids[..2]);
        firstPage.NextCursor.ShouldNotBeNull();

        using var second = await api.Http.GetAsync($"{Users}?q={marker}&limit=2&cursor={firstPage.NextCursor}", CancellationToken);
        var secondPage = await second.ReadAsync<Page<UserResponse>>();
        secondPage.Items.Select(user => user.Id).ShouldBe(ids[2..]);
        secondPage.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListUsers_FindsByEmail_AndFiltersByStatus()
    {
        using var api = await server.CreateApiClientAsync();
        var active = await server.CreateUserAsync();
        var suspended = await server.CreateUserAsync();
        using var suspend = await api.Http.PostAsync($"{Users}/{suspended.Id}/suspend");

        using var byEmail = await api.Http.GetAsync($"{Users}?q={Uri.EscapeDataString(active.Email.ToUpperInvariant())}", CancellationToken);
        (await byEmail.ReadAsync<Page<UserResponse>>()).Items.ShouldHaveSingleItem().Id.ShouldBe(active.Id);

        using var bySuspended = await api.Http.GetAsync($"{Users}?q={suspended.Email}&status=suspended", CancellationToken);
        (await bySuspended.ReadAsync<Page<UserResponse>>()).Items.ShouldHaveSingleItem().Status.ShouldBe(UserStatus.Suspended);

        using var byActive = await api.Http.GetAsync($"{Users}?q={suspended.Email}&status=active", CancellationToken);
        (await byActive.ReadAsync<Page<UserResponse>>()).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cursor=not-a-cursor", "common.invalid_cursor")]
    [InlineData("status=7", "common.invalid_parameter")]
    [InlineData("status=Active", "common.invalid_parameter")]
    [InlineData("limit=many", "request.invalid")]
    public async Task ListUsers_WithInvalidParameter_IsBadRequest(string query, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.GetAsync($"{Users}?{query}", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task SuspendedUser_LosesApiAccessAtOnce_UntilReactivated()
    {
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);
        using var asAdmin = server.WithToken(await server.UserAccessTokenAsync(admin));
        using var before = await asAdmin.GetAsync(Users, CancellationToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var api = await server.CreateApiClientAsync();
        using var suspend = await api.Http.PostAsync($"{Users}/{admin.Id}/suspend");
        suspend.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await asAdmin.GetAsync(Users, CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var reactivate = await api.Http.PostAsync($"{Users}/{admin.Id}/reactivate");
        reactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var fetched = await api.Http.GetAsync($"{Users}/{admin.Id}", CancellationToken);
        (await fetched.ReadAsync<UserResponse>()).Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task SetRoles_ReplacesTheRoles_WithinTheCallersOwnAccess()
    {
        using var support = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.UsersRead, SystemPermissions.UsersWrite));
        var (role, _) = await server.CreateRoleAsync();
        var user = await server.CreateUserAsync();

        using var granted = await support.Http.PutJsonAsync($"{Users}/{user.Id}/roles", new SetRolesRequest { Roles = [role] });
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await granted.ReadAsync<UserResponse>()).Roles.ShouldBe([role]);

        using var escalated = await support.Http.PutJsonAsync($"{Users}/{user.Id}/roles", new SetRolesRequest { Roles = [role, SystemRoles.Admin] });
        escalated.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalated.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");

        using var unknown = await support.Http.PutJsonAsync($"{Users}/{user.Id}/roles", new SetRolesRequest { Roles = ["no-such-role"] });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.ReadProblemCodeAsync()).ShouldBe("access.unknown_role");

        using var cleared = await support.Http.PutJsonAsync($"{Users}/{user.Id}/roles", new SetRolesRequest { Roles = [] });
        (await cleared.ReadAsync<UserResponse>()).Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccountsWithMoreAccess_CannotBeManaged()
    {
        using var support = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.UsersRead, SystemPermissions.UsersWrite));
        var admin = await server.CreateUserAsync();
        await server.AssignToUserAsync(admin.Id, SystemRoles.Admin);

        using var suspend = await support.Http.PostAsync($"{Users}/{admin.Id}/suspend");
        using var update = await support.Http.SendJsonAsync(HttpMethod.Patch, $"{Users}/{admin.Id}", """{ "givenName": "Mallory" }""");
        using var roles = await support.Http.PutJsonAsync($"{Users}/{admin.Id}/roles", new SetRolesRequest { Roles = [] });
        using var delete = await support.Http.DeleteAsync($"{Users}/{admin.Id}", CancellationToken);

        foreach (var response in new[] { suspend, update, roles, delete })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemCodeAsync()).ShouldBe("access.privilege_escalation");
        }
    }

    [Fact]
    public async Task Changes_AreAudited_WithTheServiceClientAsActor()
    {
        using var api = await server.CreateApiClientAsync();

        using var created = await api.Http.PostJsonAsync(Users, new CreateUserRequest { Email = NewEmail() });
        var userId = (await created.ReadAsync<UserResponse>()).Id.ToString();

        var audit = await server.QueryDatabaseAsync(context => context.AuditEvents
            .SingleAsync(audit => audit.SubjectId == userId && audit.Action == AuditActions.UserCreated, CancellationToken));
        audit.ActorType.ShouldBe(AuditActorType.Client);
        audit.ActorId.ShouldBe(api.ClientId);
    }

    [Fact]
    public async Task VerifyEmail_MarksTheAddressVerified()
    {
        var user = await server.CreateUserAsync(emailConfirmed: false);
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostAsync($"{Users}/{user.Id}/verify-email");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var fetched = await api.Http.GetAsync($"{Users}/{user.Id}", CancellationToken);
        (await fetched.ReadAsync<UserResponse>()).EmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task SendPasswordReset_EmailsTheUserALink()
    {
        var user = await server.CreateUserAsync();
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostAsync($"{Users}/{user.Id}/send-password-reset");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        CapturingEmailSender.LinkIn(await server.Emails.WaitForAsync(user.Email, "Reset your")).ShouldStartWith("/reset-password");
    }

    [Fact]
    public async Task UnknownUser_IsNotFound()
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.PostAsync($"{Users}/{Guid.CreateVersion7()}/suspend");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemCodeAsync()).ShouldBe("user.not_found");
    }

    private static string NewEmail() => $"api-{Guid.NewGuid():N}@example.com";

    private static string NewMarker() => $"marker{Guid.NewGuid():N}";
}
