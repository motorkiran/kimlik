using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Diagnostics;

public sealed class UnreachableDatabaseTests
{
    [Fact]
    public async Task Readiness_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        await using var factory = new UnreachableDatabaseFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Liveness_ReturnsHealthy_WhenDatabaseIsUnreachable()
    {
        await using var factory = new UnreachableDatabaseFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Nothing listens on port 1, so connections are refused immediately.
                ["ConnectionStrings:Kimlik"] = "Host=127.0.0.1;Port=1;Database=kimlik;Username=kimlik;Password=kimlik",
                ["Kimlik:Database:MigrateOnStartup"] = "false",
            }));
    }
}
