# Kimlik

Kimlik ("identity" in Turkish) is an open-source, self-hosted identity and access management server. It is being built as a standards-compliant OpenID Connect provider with users, organizations, roles and permissions, plans and entitlements, multi-factor authentication, API keys and webhooks.

> **Status:** early development. Kimlik is a working OpenID Connect provider with hosted sign-in pages (milestone M1); access control and the Management API (milestone M2) are in progress. Not ready for production use.

The [design document](docs/design.md) describes the vision, scope, architecture and roadmap.

## Use Kimlik from .NET

Protect an API with `Kimlik.AspNetCore`: it validates Kimlik access tokens for the API's audience and checks permissions.

```csharp
builder.Services.AddKimlik(options =>
{
    options.Authority = new Uri("https://id.example.com/");
    options.Audience = "invoices-api";
});

app.MapGet("/invoices", (KimlikUser caller) => ...).RequirePermission("invoices:read");
```

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
| `Kimlik__Branding__*` | Product name, logo and accent color of the hosted pages and emails |

Behind a TLS-terminating proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.

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
