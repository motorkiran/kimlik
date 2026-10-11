using Kimlik.Admin;
using Kimlik.Application;
using Kimlik.Infrastructure;
using Kimlik.Infrastructure.Persistence;
using Kimlik.Infrastructure.Settings;
using Kimlik.Server.Admin;
using Kimlik.Server.Api;
using Kimlik.Server.Diagnostics;
using Kimlik.Server.Hosting;
using Kimlik.Server.Identity;
using Kimlik.Server.Oidc;
using Kimlik.Server.SocialLogin;
using Kimlik.Server.Ui;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Settings changed at runtime sit between the configuration files and the environment (ADR 0002).
builder.AddStoredSettings();

builder.AddTelemetry();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddSignInSession();
builder.Services.AddOidcServer();
builder.Services.AddSocialLogin();
builder.Services.AddBrowserClientCors();
builder.Services.AddManagementApi();
builder.Services.AddHostedUi();
builder.Services.AddKimlikRateLimiting();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
builder.Services.AddHealthProbes();
builder.Services.AddAdminPanel();

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

await app.Services.LoadStoredSettingsAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

// Once a browser has reached Kimlik over HTTPS, it never tries plain HTTP again. Subdomains are left out: Kimlik may
// run on a domain whose other hosts it does not own.
if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.RequireHttps)
{
    app.UseHsts();
}

// Each instance can leave the admin panel out, so that only an internal one serves it.
var adminPanel = app.Configuration.IsAdminPanelEnabled();
if (adminPanel)
{
    app.UseAdminContentSecurityPolicy();
}

app.UseHealthProbes();
app.UseReadinessGate();

app.UseRequestLocalization();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapCultureSwitch();
app.MapBrandingStylesheet();
app.MapTokenEndpoint();
app.MapUserInfoEndpoint();
app.MapManagementApi();

if (adminPanel)
{
    app.MapKimlikAdmin();
}

await app.RunAsync();
