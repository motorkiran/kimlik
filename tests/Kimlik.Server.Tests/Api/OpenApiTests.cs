using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Kimlik.Server.Tests.Api;

public sealed class OpenApiTests(KimlikServerFixture server)
{
    /// <summary>Set to rewrite the snapshot after an intended change to the API.</summary>
    private const string UpdateSnapshotVariable = "KIMLIK_UPDATE_SNAPSHOTS";

    [Fact]
    public async Task Document_MatchesTheSnapshot()
    {
        using var http = server.CreateClient();
        var document = await http.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var snapshotPath = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateSnapshotVariable) is not null)
        {
            await File.WriteAllTextAsync(snapshotPath, document, TestContext.Current.CancellationToken);
        }

        File.Exists(snapshotPath).ShouldBeTrue($"No snapshot yet; run the tests with {UpdateSnapshotVariable}=1 to create it.");
        var snapshot = await File.ReadAllTextAsync(snapshotPath, TestContext.Current.CancellationToken);

        JsonNode.DeepEquals(JsonNode.Parse(document), JsonNode.Parse(snapshot)).ShouldBeTrue(
            $"The Management API changed. Make sure the change is not breaking, then run the tests with {UpdateSnapshotVariable}=1 "
            + "and commit the new snapshot.");
    }

    [Fact]
    public async Task Document_DescribesOnlyTheApi_AndThePermissionOfEachOperation()
    {
        using var http = server.CreateClient();

        var document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;

        document["paths"]!.AsObject().Select(path => path.Key).ShouldAllBe(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));
        var listUsers = document["paths"]!["/api/v1/users"]!["get"]!;
        listUsers["description"]!.GetValue<string>().ShouldContain("kimlik.users:read");
        listUsers["security"]![0]!["Bearer"].ShouldNotBeNull();
    }

    [Fact]
    public async Task ApiReference_IsServed()
    {
        using var http = server.CreateClient();

        using var response = await http.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    private static string SnapshotPath() => Path.Combine(
        typeof(OpenApiTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(metadata => metadata.Key == "ProjectDirectory").Value!,
        "Api",
        "openapi-v1.json");
}
