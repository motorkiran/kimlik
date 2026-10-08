using System.Reflection;
using System.Text.Json.Nodes;
using Kimlik.Infrastructure.Provisioning;
using Kimlik.Server.Tests.Oidc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Provisioning;

/// <summary>Each test starts Kimlik on a database of its own, with a provisioning file applied at startup.</summary>
public sealed class ProvisioningFileTests(KimlikServerFixture server)
{
    private const string WorkerSecret = "a worker secret from configuration";

    /// <summary>Set to rewrite the committed JSON Schema after a change to the provisioning document.</summary>
    private const string UpdateSnapshotVariable = "KIMLIK_UPDATE_SNAPSHOTS";

    [Fact]
    public async Task File_IsAppliedAtStartup_WithSecretsFromConfiguration()
    {
        var file = await WriteFileAsync("""
            {
              // Comments and trailing commas are fine in a hand-written file.
              "apiResources": [{ "scope": "reports", "displayName": "Read your reports" }],
              "clients": [
                {
                  "clientId": "report-worker",
                  "displayName": "Report worker",
                  "type": "service",
                  "scopes": ["reports"],
                  "clientSecret": "${Workers:ReportSecret}",
                },
              ],
            }
            """);

        await using var kimlik = await StartAsync(file, new() { ["Workers:ReportSecret"] = WorkerSecret });

        using var http = kimlik.CreateClient();
        (await http.RequestClientCredentialsTokenAsync(new TestClient("report-worker", WorkerSecret), "reports")).ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("""{ "permisions": [] }""", "permisions")]
    [InlineData("""{ "clients": [{ "clientId": "w", "displayName": "W", "type": "service", "clientSecret": "${Missing:Secret}" }] }""", "Missing:Secret")]
    [InlineData("""{ "roles": [{ "key": "kimlik-root", "name": "Root" }] }""", "access.reserved_key")]
    public async Task InvalidFile_StopsStartup(string content, string reason)
    {
        var file = await WriteFileAsync(content);

        var exception = await Should.ThrowAsync<Exception>(() => StartAsync(file, []));

        exception.ToString().ShouldContain(reason);
    }

    [Fact]
    public async Task Schema_MatchesTheCommittedOne()
    {
        var path = Path.Combine(ProjectDirectory(), "..", "..", "docs", "schemas", "provisioning.schema.json");
        var schema = ProvisioningFile.Schema().ToJsonString(new() { WriteIndented = true }) + "\n";

        if (Environment.GetEnvironmentVariable(UpdateSnapshotVariable) is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, schema, TestContext.Current.CancellationToken);
        }

        File.Exists(path).ShouldBeTrue($"No schema yet; run the tests with {UpdateSnapshotVariable}=1 to create it.");
        JsonNode.DeepEquals(JsonNode.Parse(schema), JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))).ShouldBeTrue(
            $"The provisioning document changed. Run the tests with {UpdateSnapshotVariable}=1 and commit the new schema.");
    }

    private async Task<WebApplicationFactory<Program>> StartAsync(string file, Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Kimlik"] = server.ConnectionStringFor($"provisioning_{Guid.NewGuid():N}");
        settings["Kimlik:Provisioning:FilePath"] = file;

        var kimlik = server.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));

        try
        {
            await KimlikServerFixture.WaitUntilReadyAsync(kimlik);
            return kimlik;
        }
        catch
        {
            await kimlik.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> WriteFileAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kimlik-provisioning-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        return path;
    }

    private static string ProjectDirectory() =>
        typeof(ProvisioningFileTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(metadata => metadata.Key == "ProjectDirectory").Value!;
}
