# Kimlik.AspNetCore

Protects ASP.NET Core APIs with [Kimlik](https://github.com/motorkiran/kimlik), an open-source, self-hosted identity and access management server.

- Validates Kimlik access tokens for your API's audience, bearer or bound to the client's key with DPoP, and optionally Kimlik API keys.
- Authorizes by permission (`RequirePermission`) and by the caller's plan (`RequireFeature`, `IKimlikEntitlements` for limits).
- Gives endpoints the caller as `KimlikUser`: user or service client, permissions, organization and plan, and the administrator acting as the user, if one is.
- Checks the signatures of Kimlik's webhooks (`KimlikWebhook`).

```csharp
builder.Services.AddKimlik(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.Audience = "invoices-api";
});

app.MapGet("/invoices", (KimlikUser caller) => ...).RequirePermission("invoices:read");
```

Access tokens bound to a client's key (DPoP, RFC 9449) come as `Authorization: DPoP …` with a proof in the `DPoP` header, which is checked against the request's method and URL, and accepted once; a bound token is refused as a bearer token. Behind a proxy, use the forwarded headers middleware, so that the request's URL is the one clients called. Proofs are remembered by each instance for the five minutes they are valid; an API with several instances relies on the short window and TLS for the rest.

API keys, features and plans need [Kimlik.Client](https://www.nuget.org/packages/Kimlik.Client), registered as a service client of your API. See the [Kimlik README](https://github.com/motorkiran/kimlik#use-kimlik-from-net).
