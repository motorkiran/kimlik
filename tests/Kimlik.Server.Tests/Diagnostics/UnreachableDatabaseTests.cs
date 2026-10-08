using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kimlik.Server.Tests.Diagnostics;

public sealed class UnreachableDatabaseTests
{
    // Nothing listens on port 1, so connections are refused immediately.
    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=kimlik;Username=kimlik;Password=kimlik";

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

    [Fact]
    public async Task OidcRequests_ReturnServiceUnavailable_UntilTokenKeysAreLoaded()
    {
        await using var factory = new UnreachableDatabaseFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private sealed class UnreachableDatabaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment(TestConfiguration.Environment)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = TestConfiguration.Create(UnreachableConnectionString);
                settings["Kimlik:Database:MigrateOnStartup"] = "false";
                configuration.AddInMemoryCollection(settings);
            });
    }
}
