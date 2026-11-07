# Kimlik

[![CI](https://github.com/motorkiran/kimlik/actions/workflows/ci.yml/badge.svg)](https://github.com/motorkiran/kimlik/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/motorkiran/kimlik)](https://github.com/motorkiran/kimlik/releases/latest)
[![License](https://img.shields.io/github/license/motorkiran/kimlik)](LICENSE)

Kimlik ("identity" in Turkish) is an open-source, self-hosted identity and access management server for developers: a standards-compliant OpenID Connect provider with users, organizations, roles and permissions, plans and entitlements, multi-factor authentication, API keys and webhooks. It is one ASP.NET Core application and a PostgreSQL database, licensed under Apache 2.0.

> **Status:** Kimlik covers the scope of its [design](docs/design.md) and is tested end to end in CI, but it is young: it has not run in production yet, and until 1.0, minor versions may change its APIs. The latest release is [0.7.0](https://github.com/motorkiran/kimlik/releases/latest); the [changelog](CHANGELOG.md) says what each release brought, and what the main branch has since.

## What it does

- **Standards.** OpenID Connect and OAuth 2.0 as the current best practice (RFC 9700) has them: authorization code with PKCE, refresh token rotation with reuse detection, client credentials, device authorization, token exchange, pushed authorization requests, `private_key_jwt`, DPoP-bound tokens, introspection and revocation, RP-initiated and back-channel logout, and step-up with `acr_values`. JWT access tokens (RFC 9068) with rotating signing keys. Prepared for the OpenID Foundation's Basic, Form Post, Config and logout certification profiles.
- **Sign-in.** Hosted, brandable pages with passwords (checked against known breaches), passkeys, email codes and links, SMS codes, authenticator apps and recovery codes, CAPTCHA, Google, Microsoft, Apple and GitHub, and enterprise single sign-on over OpenID Connect or SAML per organization. Or sign-in pages of your own, through login challenges.
- **Access.** Global and organization roles that become `permissions` in tokens, organizations with invitations and an active organization per token, and API keys for users and organizations.
- **Plans.** Features, limits, add-ons, per-customer overrides and usage metering, fed by your billing system through the API, so that APIs gate features with the `plan` claim and the SDK.
- **Operations.** A REST Management and Account API with an OpenAPI document, a provisioning file for configuration as code, webhooks (Standard Webhooks), an audit log, an admin panel with impersonation, personal data export, OpenTelemetry, a container image and a Helm chart.
- **SDKs.** `Kimlik.AspNetCore` and `Kimlik.Client` for .NET, `@kimlik/node` for Node.js backends, and `@kimlik/elements`, web components for organization management in your own pages. Until the names are reserved on NuGet and npm, each release carries them as packages.

## Documentation

| | |
| --- | --- |
| [Local development](#local-development) | Running Kimlik from source, testing, the essential settings |
| [docs/configuration.md](docs/configuration.md) | Every setting |
| [docs/deployment.md](docs/deployment.md) | Production: the image, proxies, migrations, several instances, backups, hardening |
| [charts/kimlik](charts/kimlik) | The Helm chart for Kubernetes |
| [docs/design.md](docs/design.md) | Vision, scope, architecture, protocol design and roadmap |
| [docs/certification.md](docs/certification.md) | Running the OpenID conformance suite |
| [samples](samples) | A single-page app and API protected by Kimlik |
| [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md) | Contributing, and reporting vulnerabilities |

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

Access tokens that clients bound to their keys with DPoP (RFC 9449) are accepted as `Authorization: DPoP …` with a proof, and refused as bearer tokens; `@kimlik/node` does the same. A client gets them by sending a proof with its token requests, and an administrator can require that of a client (`requireDpop`).

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

## Use Kimlik from Node.js

`@kimlik/node`, in [`sdk/js`](sdk/js), does the same for Node.js backends. It is not on npm yet; until it is, install the tarball attached to each [release](https://github.com/motorkiran/kimlik/releases).

```js
import express from "express";
import { createKimlik, kimlikMiddleware, requirePermission, verifyWebhook } from "@kimlik/node";

const kimlik = createKimlik({
  authority: "https://id.example.com/",
  audience: "invoices-api",
  // Only for API keys, entitlements and the Management API:
  clientId: "invoices-api",
  clientSecret: process.env.KIMLIK_CLIENT_SECRET,
});

app.get("/invoices", kimlikMiddleware(kimlik), requirePermission("invoices:read"), (req, res) => {
  res.json({ caller: req.kimlik.userId, organization: req.kimlik.organizationId });
});

const { data: user } = await kimlik.client.GET("/api/v1/users/{id}", { params: { path: { id } } });
const event = verifyWebhook({ headers: req.headers, body: rawBody, secret }); // null unless Kimlik signed it
```

## Organization management in your own pages

`@kimlik/elements`, in [`sdk/elements`](sdk/elements), is a set of web components for apps that want organization management inside their own pages rather than on Kimlik's. They list the user's organizations and invitations, manage members, roles and invitations, and change or delete an organization, as the user's roles allow. They call Kimlik with the user's token for the `kimlik.organizations` scope, which reaches nothing else. Releases carry it as a tarball until it is on npm.

```html
<kimlik-organization-members organization="0199c3a1-1111-7d3c-8b4a-5e6f70819203"></kimlik-organization-members>
<script type="module">
  import { configureKimlik } from "@kimlik/elements";
  configureKimlik({ authority: "https://id.example.com/", accessToken: () => getAccessToken() });
</script>
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

Behind a TLS-terminating proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. [docs/configuration.md](docs/configuration.md) lists every setting, including the product settings that administrators can change at runtime in the admin panel, and [docs/deployment.md](docs/deployment.md) covers running Kimlik in production: the image, reverse proxies, migrations, several instances, backups and a hardening checklist. A [Helm chart](charts/kimlik) installs Kimlik on Kubernetes. [docs/certification.md](docs/certification.md) runs the OpenID Foundation's conformance suite against a deployment.

### Sign-in with other providers

People can sign up and sign in with an account at Google, Microsoft, Apple or GitHub. A provider appears on the sign-in page once its client ID is set, in the configuration or at runtime in the admin panel:

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

Providers that speak SAML 2.0 instead, such as AD FS, get a connection with `"protocol": "saml"`. Add Kimlik to the provider as a SAML app with `{PublicUrl}/signin/sso/saml` as its entity ID and `{PublicUrl}/signin/sso/saml/acs` as its assertion consumer service URL, or import `{PublicUrl}/signin/sso/saml/metadata`. Have the provider send the email address as the name ID or an `email` attribute. Then enter the provider's entity ID, its sign-on URL and its signing certificate. SAML needs Kimlik on HTTPS, because the provider posts its response from its own site.

While the connection is enabled, people with addresses in those domains sign in only through the provider: the sign-in page sends them there as soon as they enter their address, and other ways of signing in lead there too. Their first sign-in links the account with the same address, or creates one whatever the registration mode, and makes them a member of the organization. Disabling the connection restores the other ways of signing in. Because a connection can sign in anyone in its domains, administrators included, managing connections takes every installation-wide system permission; claim only domains the organization owns.

### Sign-in pages of your own

Kimlik's hosted pages take a logo, colors and a stylesheet. To build the sign-in pages entirely yourself, on your own domain, set `Kimlik__Interactions__LoginUrl` (or **Login app** in the admin panel's settings) to your login page. Authorization requests that need a sign-in then go there with a `login_challenge`, and your backend answers it through the Management API, as a service client with `kimlik.interactions:write`:

1. `GET /api/v1/interactions/login/{challenge}` says which app asks, for which scopes, and whether the sign-in must take a second factor or a passkey.
2. `POST …/password` and `POST …/second-factor` check the person's Kimlik password and authenticator or recovery code, if your pages use those. Each challenge allows ten checks, and failures count toward the account's lockout.
3. `POST …/accept` with the user's ID and the methods they used, such as `["pwd", "otp"]`, or `POST …/reject`, returns the URL to send the browser to. Kimlik then finishes the sign-in, in the browser the challenge was sent from, and the app gets its tokens.

Kimlik still applies its rules: the methods must meet the MFA policies and the app's `acr_values`, and a login app cannot sign in users who hold system permissions it does not hold. Those users, and people who sign in with passkeys, use the hosted pages at the challenge's `hostedSignInUrl`. `Kimlik.Client` covers these calls as `kimlik.Interactions`.

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

## Contributing

Issues and pull requests are welcome; [CONTRIBUTING.md](CONTRIBUTING.md) explains how to build, test and propose changes, and the [code of conduct](CODE_OF_CONDUCT.md) applies. Report vulnerabilities privately, as [SECURITY.md](SECURITY.md) describes.

## License

Kimlik is licensed under the [Apache License 2.0](LICENSE).
