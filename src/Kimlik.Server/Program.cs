using Kimlik.Infrastructure;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Server.Diagnostics;
using Kimlik.Server.Oidc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.Services.AddInfrastructure();
builder.Services.AddOidcServer();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHealthProbes();

var app = builder.Build();

// Fail fast on invalid configuration, before anything touches the database.
app.Services.GetRequiredService<IStartupValidator>().Validate();

if (args is ["migrate", ..])
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseHealthProbes();
app.UseReadinessGate();

app.UseAuthentication();
app.UseAuthorization();

app.MapTokenEndpoint();

await app.RunAsync();
