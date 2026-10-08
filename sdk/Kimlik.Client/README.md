# Kimlik.Client

A typed client for the Management API of [Kimlik](https://github.com/motorkiran/kimlik), an open-source, self-hosted identity and access management server: users, roles and permissions, clients, organizations, plans and subscriptions, API keys, webhooks, the audit log and provisioning.

It signs in as a service client with client credentials, and gets and renews its access tokens by itself.

```csharp
builder.Services.AddKimlikClient(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.ClientId = "backend";
    options.ClientSecret = builder.Configuration["Kimlik:ClientSecret"];
});

var user = await kimlik.Users.CreateAsync(new CreateUserRequest { Email = "ada@example.com" }, cancellationToken);
```

Failed calls throw `KimlikApiException` with the problem's code, such as `user.not_found`.
