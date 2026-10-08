# Kimlik.AspNetCore

Protects ASP.NET Core APIs with [Kimlik](https://github.com/motorkiran/kimlik), an open-source, self-hosted identity and access management server.

- Validates Kimlik access tokens for your API's audience, and optionally Kimlik API keys.
- Authorizes by permission (`RequirePermission`) and by the caller's plan (`RequireFeature`, `IKimlikEntitlements` for limits).
- Gives endpoints the caller as `KimlikUser`: user or service client, permissions, organization and plan.
- Checks the signatures of Kimlik's webhooks (`KimlikWebhook`).

```csharp
builder.Services.AddKimlik(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.Audience = "invoices-api";
});

app.MapGet("/invoices", (KimlikUser caller) => ...).RequirePermission("invoices:read");
```

API keys, features and plans need [Kimlik.Client](https://www.nuget.org/packages/Kimlik.Client), registered as a service client of your API. See the [Kimlik README](https://github.com/motorkiran/kimlik#use-kimlik-from-net).
