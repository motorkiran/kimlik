using System.Net;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Access;
using Kimlik.Server.Tests.Access;

namespace Kimlik.Server.Tests.Api;

public sealed class AuditEventApiTests(KimlikServerFixture server)
{
    private const string AuditEvents = "/api/v1/audit-events";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AuditTrail_ShowsWhatHappenedToASubject_NewestFirst_PageByPage()
    {
        using var api = await server.CreateApiClientAsync();
        using var created = await api.Http.PostJsonAsync("/api/v1/users", new CreateUserRequest { Email = $"audit-{Guid.NewGuid():N}@example.com" });
        var user = await created.ReadAsync<UserResponse>();
        using var suspended = await api.Http.PostAsync($"/api/v1/users/{user.Id}/suspend");

        using var first = await api.Http.GetAsync($"{AuditEvents}?subjectType=user&subjectId={user.Id}&limit=1", CancellationToken);
        var firstPage = await first.ReadAsync<Page<AuditEventResponse>>();
        var latest = firstPage.Items.ShouldHaveSingleItem();
        latest.Action.ShouldBe("user.suspended");
        latest.ActorType.ShouldBe(AuditActorType.Client);
        latest.ActorId.ShouldBe(api.ClientId);

        using var second = await api.Http.GetAsync($"{AuditEvents}?subjectType=user&subjectId={user.Id}&limit=1&cursor={firstPage.NextCursor}", CancellationToken);
        var secondPage = await second.ReadAsync<Page<AuditEventResponse>>();
        secondPage.Items.ShouldHaveSingleItem().Action.ShouldBe("user.created");
        secondPage.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Filters_Combine()
    {
        using var api = await server.CreateApiClientAsync();
        using var role = await api.Http.PostJsonAsync("/api/v1/roles", new CreateRoleRequest { Key = $"auditor-{Guid.NewGuid():N}", Name = "Auditor" });
        var roleId = (await role.ReadAsync<RoleResponse>()).Id;

        using var byActor = await api.Http.GetAsync($"{AuditEvents}?actorType=client&actorId={api.ClientId}&action=role.created", CancellationToken);
        var roleCreated = (await byActor.ReadAsync<Page<AuditEventResponse>>()).Items.ShouldHaveSingleItem();
        roleCreated.SubjectId.ShouldBe(roleId.ToString());
        roleCreated.Data!.Value.GetProperty("key").GetString().ShouldStartWith("auditor-");

        var later = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        using var inTheFuture = await api.Http.GetAsync($"{AuditEvents}?actorId={api.ClientId}&from={later}", CancellationToken);
        (await inTheFuture.ReadAsync<Page<AuditEventResponse>>()).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("actorType=robot", "common.invalid_parameter")]
    [InlineData("from=yesterday", "request.invalid")]
    [InlineData("cursor=nope", "common.invalid_cursor")]
    public async Task InvalidFilter_IsBadRequest(string query, string code)
    {
        using var api = await server.CreateApiClientAsync();

        using var response = await api.Http.GetAsync($"{AuditEvents}?{query}", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task AuditTrail_RequiresTheAuditPermission()
    {
        using var api = await server.CreateApiClientAsync(await server.CreateRoleWithAsync(SystemPermissions.UsersRead));

        using var response = await api.Http.GetAsync(AuditEvents, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
