using System.Net;
using Kimlik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Kimlik.Server.Tests.KimlikServerFixture))]

namespace Kimlik.Server.Tests;

/// <summary>
/// Hosts Kimlik in memory against a disposable PostgreSQL container shared by every test in the assembly.
/// </summary>
public sealed class KimlikServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PostgreSqlImage = "docker.io/library/postgres:18-alpine";

    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgreSqlImage).Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await WaitUntilReadyAsync();
    }

    /// <summary>Runs <paramref name="action"/> with a fresh database context in its own scope.</summary>
    public async Task<T> QueryDatabaseAsync<T>(Func<KimlikDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<KimlikDbContext>());
    }

    /// <summary>Runs <paramref name="action"/> with services resolved from a fresh scope.</summary>
    public async Task<T> WithServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment(TestConfiguration.Environment)
        .ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(TestConfiguration.Create(_postgres.GetConnectionString())));

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // Token keys load in the background after startup; tests start once the instance reports ready.
    private async Task WaitUntilReadyAsync()
    {
        using var client = CreateClient();
        using var timeout = new CancellationTokenSource(ReadinessTimeout);

        while (true)
        {
            using var response = await client.GetAsync("/health/ready", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
