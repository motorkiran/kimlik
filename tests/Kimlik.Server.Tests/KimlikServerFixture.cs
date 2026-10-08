using System.Net;
using Kimlik.Application.Abstractions;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

    /// <summary>Every email the server sends during the test run.</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>The webhook endpoints of the test run, which every host sends its webhooks to.</summary>
    internal Webhooks.TestWebhookReceiver Webhooks { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await WaitUntilReadyAsync(this);
        await WithServicesAsync(Oidc.TestClients.CreateApiScopeAsync);
    }

    /// <summary>A connection string for another database on the same server, which Kimlik creates on startup.</summary>
    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

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

    /// <inheritdoc cref="WithServicesAsync{T}(Func{IServiceProvider, Task{T}})"/>
    public async Task WithServicesAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment(TestConfiguration.Environment)
        .ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(TestConfiguration.Create(_postgres.GetConnectionString())))
        .ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);
            services.AddHttpClient(WebhookSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(Webhooks.CreateHandler);
        });

    /// <summary>
    /// A host that checks the security stamp of sign-in sessions on every request instead of every few minutes, as
    /// if each request came after the interval.
    /// </summary>
    public async Task<WebApplicationFactory<Program>> WithStrictSessionsAsync()
    {
        var strict = WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));

        await WaitUntilReadyAsync(strict);
        return strict;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Token keys load in the background after startup, so tests start once the instance reports ready.
    /// Also used for hosts derived with <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>.
    /// </summary>
    public static async Task WaitUntilReadyAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
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
