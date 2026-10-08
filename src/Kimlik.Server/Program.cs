using Kimlik.Application;
using Kimlik.Infrastructure;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Server.Diagnostics;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Kimlik.Server.Oidc;
using Kimlik.Server.Ui;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddSignInSession();
builder.Services.AddOidcServer();
builder.Services.AddHostedUi();
builder.Services.AddKimlikRateLimiting();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHealthProbes();

var app = builder.Build();

// Fail fast on invalid configuration, before anything touches the database.
app.Services.GetRequiredService<IStartupValidator>().Validate();

if (args is ["migrate", ..])
{
    await app.Services.PrepareDatabaseAsync();
    return;
}

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
{
    await app.Services.PrepareDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

app.UseHealthProbes();
app.UseReadinessGate();

app.UseRequestLocalization();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapCultureSwitch();
app.MapTokenEndpoint();
app.MapUserInfoEndpoint();

await app.RunAsync();
