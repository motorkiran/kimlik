using System.ComponentModel.DataAnnotations;
using System.Net;
using Kimlik.AspNetCore;
using Kimlik.AspNetCore.Authorization;
using Kimlik.Client;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Kimlik");
var authority = new Uri(settings["Authority"]!);

// Accept Kimlik access tokens issued for this API.
builder.Services.AddKimlik(options =>
{
    options.Authority = authority;
    options.Audience = settings["Audience"];
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
});

// Call the Kimlik Management API as this API's own service client.
builder.Services.AddKimlikClient(options =>
{
    options.Authority = authority;
    options.ClientId = settings["BackendClientId"];
    options.ClientSecret = settings["BackendClientSecret"];
});

builder.Services.AddValidation();
builder.Services.AddSingleton<InvoiceStore>();

var app = builder.Build();

// The single-page app in wwwroot signs users in with Kimlik and calls the API below with their access token.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/config.json", () => new { Authority = authority, ClientId = settings["SpaClientId"] });

var api = app.MapGroup("/api");

api.MapGet("/me", (KimlikUser caller) => caller).RequireAuthorization();

api.MapGet("/invoices", (InvoiceStore invoices) => invoices.List()).RequirePermission("invoices:read");

api.MapPost("/invoices", (NewInvoice invoice, KimlikUser caller, InvoiceStore invoices) =>
        TypedResults.Ok(invoices.Add(invoice, caller.Email ?? caller.Subject)))
    .RequirePermission("invoices:write");

if (app.Environment.IsDevelopment())
{
    // For the demo only: anyone signed in can become an accountant. The API holds kimlik.users:write through
    // its service client, so it can change the caller's roles; the new permissions arrive with the next token.
    api.MapPost("/demo/become-accountant", async (KimlikUser caller, KimlikClient kimlik, CancellationToken cancellationToken) =>
    {
        if (caller.UserId is not { } userId)
        {
            return Results.BadRequest();
        }

        try
        {
            var user = await kimlik.Users.GetAsync(userId, cancellationToken);
            await kimlik.Users.SetRolesAsync(userId, [.. user.Roles.Union(["accountant"])], cancellationToken);
            return Results.NoContent();
        }
        catch (KimlikApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            // Kimlik keeps the API within its own access: it cannot change an administrator, for one.
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }).RequireAuthorization();
}

await app.RunAsync();

internal sealed record Invoice(Guid Id, string Customer, decimal Amount, string CreatedBy, DateTimeOffset CreatedAt);

internal sealed record NewInvoice([property: Required, StringLength(100)] string Customer, [property: Range(0.01, 1_000_000)] decimal Amount);

/// <summary>Invoices kept in memory; a real API would use a database.</summary>
internal sealed class InvoiceStore
{
    private readonly Lock _lock = new();
    private readonly List<Invoice> _invoices =
    [
        new(Guid.CreateVersion7(), "Lovelace Analytical Engines", 1843.00m, "seed", DateTimeOffset.UtcNow),
        new(Guid.CreateVersion7(), "Hopper Compilers", 1952.50m, "seed", DateTimeOffset.UtcNow),
    ];

    public IReadOnlyList<Invoice> List()
    {
        lock (_lock)
        {
            return [.. _invoices];
        }
    }

    public Invoice Add(NewInvoice invoice, string createdBy)
    {
        var added = new Invoice(Guid.CreateVersion7(), invoice.Customer.Trim(), invoice.Amount, createdBy, DateTimeOffset.UtcNow);
        lock (_lock)
        {
            _invoices.Add(added);
        }

        return added;
    }
}
