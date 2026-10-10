# Kimlik

Kimlik ("identity" in Turkish) is an open-source, self-hosted identity and access management server. It is being built as a standards-compliant OpenID Connect provider with users, organizations, roles and permissions, plans and entitlements, multi-factor authentication, API keys and webhooks.

> **Status:** early development. Kimlik is a working OpenID Connect provider with hosted sign-in pages, roles and permissions, organizations with invitations, plans and subscriptions, a Management and Account API, provisioning and a .NET SDK (milestones M1 to M4), plus two-factor authentication, sign-in with Google, Microsoft, Apple and GitHub, account pages (milestone M5), API keys and webhooks (M6), and an admin panel (M7). Version 0.1.0 (M8) was the first release, and 0.2.0 adds passkeys, email sign-in codes and links, accounts without a password, personal data export, the device authorization grant, breached-password checks, admin impersonation, `private_key_jwt` and pushed authorization requests (M9 to M16). Unreleased since then: phone numbers and SMS codes, token exchange, back-channel logout, add-ons and overrides, CAPTCHA and `acr_values` step-up, usage metering, secret rotation and enterprise single sign-on (M17 to M24). Not ready for production use yet.

The [design document](docs/design.md) describes the vision, scope, architecture and roadmap.

## Use Kimlik from .NET

The packages are not on NuGet yet; until they are, reference the projects in [`sdk/`](sdk), as the [sample API](samples/Invoices.Api) does.

Protect an API with `Kimlik.AspNetCore`: it validates Kimlik access tokens for the API's audience and checks permissions.

```csharp
builder.Services.AddKimlik(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.Audience = "invoices-api";
});

app.MapGet("/invoices", (KimlikUser caller) => ...).RequirePermission("invoices:read");
app.MapGet("/invoices/export", ...).RequireFeature("export_pdf");   // with AddKimlikEntitlements()
```

With `options.ApiKeys.Enabled = true`, the API also accepts Kimlik API keys (`Authorization: Bearer kmk_…`) that users and organizations create through the Account API. The caller looks the same as with an access token, and the SDK verifies keys through `Kimlik.Client`, as a service client holding `kimlik.api_keys:verify`, caching each answer for 30 seconds.

Manage users, roles and clients from a backend with `Kimlik.Client`, as a service client that holds the `kimlik` scope and an administrative role. It gets and renews its tokens by itself.

```csharp
builder.Services.AddKimlikClient(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.ClientId = "backend";
    options.ClientSecret = builder.Configuration["Kimlik:ClientSecret"];
});

var user = await kimlik.Users.CreateAsync(new CreateUserRequest { Email = "ada@example.com" }, cancellationToken);
```

Receive Kimlik's webhooks, such as `user.created` or `subscription.updated`, at an endpoint registered with `POST /api/v1/webhooks/endpoints`. They follow the [Standard Webhooks](https://www.standardwebhooks.com/) specification, so any of its libraries can check them too:

```csharp
app.MapPost("/webhooks/kimlik", async (HttpRequest request) =>
{
    if (await KimlikWebhook.ReadAsync(request, secret) is not { } webhookEvent)
    {
        return Results.Unauthorized();
    }

    // webhookEvent.Type, webhookEvent.Data.SubjectId…; delivery is at least once, so handle each webhook-id once.
    return Results.Ok();
});
```

The [samples](samples/README.md) show a single-page app and its API working with Kimlik end to end.

## Local development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A container engine with a Docker-compatible API (Docker Desktop, OrbStack, or Podman 5+ with a rootful machine) and Docker Compose v2

### Run from source

```bash
docker compose up -d                      # PostgreSQL and Mailpit (or: podman compose up -d)
dotnet run --project src/Kimlik.Server
```

| URL | Purpose |
|---|---|
| http://localhost:5080/admin | Admin panel, for accounts with a system permission such as the development administrator |
| http://localhost:5080/account | Account pages: profile, password, two-factor authentication, signed-in applications |
| http://localhost:5080/health/live | Liveness probe |
| http://localhost:5080/health/ready | Readiness probe (checks the database) |
| http://localhost:5080/scalar/v1 | Management API reference; the OpenAPI document is at `/openapi/v1.json` |
| http://localhost:8025 | Mailpit inbox for development email |

In development, the first start creates the administrator `admin@kimlik.localhost` with the password `development-only-password`.

The development database listens on port 5433 so that it does not clash with a locally installed PostgreSQL. To use another port, set `KIMLIK_POSTGRES_PORT` and update `ConnectionStrings:Kimlik` in `src/Kimlik.Server/appsettings.Development.json`.

### Run as a container

```bash
dotnet publish src/Kimlik.Server -t:PublishContainer   # builds the kimlik:latest image
docker compose --profile app up -d
```

Kimlik then listens on http://localhost:8080.

### Test

```bash
dotnet test
```

Integration tests start a disposable PostgreSQL container with Testcontainers, so the container engine must be running.

A snapshot of the OpenAPI document guards the Management API against accidental breaking changes. After an intended API change, update it with `KIMLIK_UPDATE_SNAPSHOTS=1 dotnet test` and commit the new snapshot.

### Configuration

Settings come from `appsettings.json` and environment variables (`Kimlik__Section__Key`). The essentials:

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Kimlik` | PostgreSQL connection string |
| `Kimlik__Server__PublicUrl` | Public base URL; the token issuer and the base of links in emails |
| `Kimlik__Security__MasterKey` | 256-bit key that encrypts secrets at rest (`openssl rand -base64 32`). Back it up. |
| `Kimlik__Bootstrap__AdminEmail`, `Kimlik__Bootstrap__AdminPassword` | The first administrator, created while the installation has none. An existing account with this address is promoted only if it has verified the address. |
| `Kimlik__Email__FromAddress`, `Kimlik__Email__Smtp__Host` | Sender and SMTP relay for verification and reset emails |
| `Kimlik__Provisioning__FilePath` | A provisioning file to apply at startup (see below) |
| `Kimlik__Accounts__*` | Registration mode, email verification, password length, lockout |
| `Kimlik__Mfa__*` | Who must use a second factor (administrators by default, or everyone) and how long a trusted browser may skip it |
| `Kimlik__Organizations__*` | Whether users can create organizations, the roles creators get, how long invitations last |
| `Kimlik__Plans__DefaultUserPlan`, `Kimlik__Plans__DefaultOrganizationPlan` | The plan of users and organizations without a current subscription |
| `Kimlik__Admin__Enabled` | Whether this instance serves the admin panel (default `true`); turn it off on public instances to serve it from an internal one only |
| `Kimlik__Webhooks__*` | Retry delays (`RetryDelays`), timeout and how long the delivery log is kept |
| `Kimlik__Audit__RetentionPeriod` | How long audit events are kept, such as `365.00:00:00` (the default) |
| `Kimlik__Branding__*` | Product name, logo and accent color of the hosted pages and emails |
| `Kimlik__SocialLogin__*` | Sign-in with Google, Microsoft, Apple and GitHub (see below) |

Behind a TLS-terminating proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. [docs/configuration.md](docs/configuration.md) lists every setting, and [docs/deployment.md](docs/deployment.md) covers running Kimlik in production: the image, reverse proxies, migrations, several instances, backups and a hardening checklist.

### Sign-in with other providers

People can sign up and sign in with an account at Google, Microsoft, Apple or GitHub. A provider appears on the sign-in page once its client ID is set:

| Provider | Settings |
|---|---|
| Google | `Kimlik__SocialLogin__Google__ClientId`, `__ClientSecret` |
| Microsoft | `Kimlik__SocialLogin__Microsoft__ClientId`, `__ClientSecret`, and `__Tenant` (`common` by default) |
| GitHub | `Kimlik__SocialLogin__GitHub__ClientId`, `__ClientSecret` |
| Apple | `Kimlik__SocialLogin__Apple__ClientId` (the Services ID), `__TeamId`, `__KeyId`, `__PrivateKey` (the PEM text of the `.p8` key) |

Register `{PublicUrl}/signin/external/callback/{provider}` as the redirect URI with the provider, such as `https://id.example.com/signin/external/callback/google`.

Kimlik never links accounts because their email addresses match: when someone signs in with a provider account whose address belongs to an existing account, they sign in to that account first and confirm the link. A new account counts its address as verified only if the provider verified it and is trusted to (`__TrustEmail`: on for Google, Apple and GitHub; off for Microsoft, which does not verify every address). Two-factor authentication applies after a provider sign-in too, and tokens report it in `amr` (`fed`, plus `otp` and `mfa`).

### Enterprise single sign-on

An organization can have its people sign in through its own identity provider over OpenID Connect, such as Microsoft Entra ID, Okta or Google Workspace. Set up an SSO connection on the organization's page in the admin panel, or through `POST /api/v1/sso-connections`:

1. Register Kimlik as a web app at the provider, with `{PublicUrl}/signin/sso/callback` as its redirect URI and the `openid`, `email` and `profile` scopes.
2. Enter the provider's issuer URL, such as `https://login.microsoftonline.com/{tenant}/v2.0`, `https://acme.okta.com` or `https://accounts.google.com`, the client ID and secret, and the organization's email domains.

While the connection is enabled, people with addresses in those domains sign in only through the provider: the sign-in page sends them there as soon as they enter their address, and other ways of signing in lead there too. Their first sign-in links the account with the same address, or creates one whatever the registration mode, and makes them a member of the organization. Disabling the connection restores the other ways of signing in. Because a connection can sign in anyone in its domains, administrators included, managing connections takes every installation-wide system permission; claim only domains the organization owns.

### Provisioning

Permissions, roles, API resources and clients can be declared in a JSON file and kept in version control:

```json
{
  "$schema": "https://raw.githubusercontent.com/motorkiran/kimlik/main/docs/schemas/provisioning.schema.json",
  "permissions": [{ "key": "invoices:read" }, { "key": "invoices:write" }],
  "roles": [{ "key": "billing-service", "name": "Billing service", "permissions": ["invoices:read"] }],
  "apiResources": [{ "scope": "invoices", "audience": "invoices-api", "displayName": "Manage your invoices" }],
  "clients": [
    { "clientId": "web", "displayName": "Web app", "type": "spa", "redirectUris": ["https://app.example.com/callback"], "scopes": ["openid", "profile", "invoices"] },
    { "clientId": "billing-worker", "displayName": "Billing worker", "type": "service", "scopes": ["invoices"], "roles": ["billing-service"], "clientSecret": "${Billing:WorkerSecret}" }
  ]
}
```

Set `Kimlik__Provisioning__FilePath` and Kimlik applies the file whenever it prepares the database: it creates what is missing and updates what differs, in one transaction, and never deletes. `${Some:Setting}` reads a client secret from configuration, such as the environment variable `Some__Setting`, so secrets stay out of the file. The same document can be applied with `POST /api/v1/provisioning`, and `GET /api/v1/provisioning` exports the current model.

### Database migrations

Migrations run at startup by default, followed by the bootstrap administrator and the provisioning file. To run all of it as a separate deployment step instead, set `Kimlik__Database__MigrateOnStartup=false` and run:

```bash
dotnet run --project src/Kimlik.Server -- migrate
```

## License

Kimlik is licensed under the [Apache License 2.0](LICENSE).
