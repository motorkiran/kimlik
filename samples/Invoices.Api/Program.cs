using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Kimlik.AspNetCore;
using Kimlik.AspNetCore.Authorization;
using Kimlik.AspNetCore.Entitlements;
using Kimlik.Client;
using Kimlik.Contracts.Management;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Kimlik");
var authority = new Uri(settings["Authority"]!);

// Accept Kimlik access tokens issued for this API, and Kimlik API keys.
builder.Services.AddKimlik(options =>
{
    options.Authority = authority;
    options.Audience = settings["Audience"];
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.ApiKeys.Enabled = true;
});

// Call the Kimlik Management API as this API's own service client, which also reads the plan definitions and
// verifies API keys.
builder.Services.AddKimlikClient(options =>
{
    options.Authority = authority;
    options.ClientId = settings["BackendClientId"];
    options.ClientSecret = settings["BackendClientSecret"];
});
builder.Services.AddKimlikEntitlements();

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

// The plan sets how many invoices each user may create.
api.MapPost("/invoices", async (NewInvoice invoice, KimlikUser caller, InvoiceStore invoices, IKimlikEntitlements entitlements, CancellationToken cancellationToken) =>
    {
        if (await entitlements.GetLimitAsync(caller, "max_invoices", cancellationToken) is { } limit && invoices.CountCreatedBy(caller.Subject) >= limit)
        {
            return Results.Problem($"Your plan allows {limit} invoices. Upgrade to create more.", statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(invoices.Add(invoice, caller.Subject, caller.Email ?? caller.Subject));
    })
    .RequirePermission("invoices:write");

// Only plans with the export_pdf feature may export.
api.MapGet("/invoices/export", (InvoiceStore invoices) =>
    {
        var csv = new StringBuilder("customer,amount,created_by\n");
        foreach (var invoice in invoices.List())
        {
            csv.Append(invoice.Customer).Append(',').Append(invoice.Amount).Append(',').Append(invoice.CreatedBy).Append('\n');
        }

        return Results.Text(csv.ToString(), "text/csv");
    })
    .RequirePermission("invoices:read")
    .RequireFeature("export_pdf");

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

    // For the demo only: a billing system would do this after a payment. The next token carries the new plan.
    api.MapPost("/demo/upgrade", async (KimlikUser caller, KimlikClient kimlik, CancellationToken cancellationToken) =>
    {
        if (caller.UserId is not { } userId)
        {
            return Results.BadRequest();
        }

        var history = await kimlik.Subscriptions.ListAsync(SubscriberType.User, userId, cancellationToken: cancellationToken);
        if (history.Items.FirstOrDefault(subscription => subscription.Status != SubscriptionStatus.Expired) is { } current)
        {
            await kimlik.Subscriptions.UpdateAsync(current.Id, new UpdateSubscriptionRequest { Plan = "pro" }, cancellationToken);
        }
        else
        {
            await kimlik.Subscriptions.CreateAsync(
                new CreateSubscriptionRequest { SubscriberType = SubscriberType.User, SubscriberId = userId, Plan = "pro" }, cancellationToken);
        }

        return Results.NoContent();
    }).RequireAuthorization();
}

await app.RunAsync();

internal sealed record Invoice(Guid Id, string Customer, decimal Amount, string CreatedBy, DateTimeOffset CreatedAt);

internal sealed record NewInvoice([property: Required, StringLength(100)] string Customer, [property: Range(0.01, 1_000_000)] decimal Amount);

/// <summary>Invoices kept in memory; a real API would use a database.</summary>
internal sealed class InvoiceStore
{
    private readonly Lock _lock = new();
    private readonly List<(Invoice Invoice, string Creator)> _invoices =
    [
        (new(Guid.CreateVersion7(), "Lovelace Analytical Engines", 1843.00m, "seed", DateTimeOffset.UtcNow), "seed"),
        (new(Guid.CreateVersion7(), "Hopper Compilers", 1952.50m, "seed", DateTimeOffset.UtcNow), "seed"),
    ];

    public IReadOnlyList<Invoice> List()
    {
        lock (_lock)
        {
            return [.. _invoices.Select(entry => entry.Invoice)];
        }
    }

    public int CountCreatedBy(string creator)
    {
        lock (_lock)
        {
            return _invoices.Count(entry => entry.Creator == creator);
        }
    }

    public Invoice Add(NewInvoice invoice, string creator, string createdBy)
    {
        var added = new Invoice(Guid.CreateVersion7(), invoice.Customer.Trim(), invoice.Amount, createdBy, DateTimeOffset.UtcNow);
        lock (_lock)
        {
            _invoices.Add((added, creator));
        }

        return added;
    }
}
