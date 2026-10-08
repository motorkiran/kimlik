using Kimlik.Infrastructure;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Server.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.Services.AddInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.AddHealthProbes();

var app = builder.Build();

// Resolving the options validates the database configuration before anything touches the database.
var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

if (args is ["migrate", ..])
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

if (databaseOptions.MigrateOnStartup)
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthProbes();

await app.RunAsync();
