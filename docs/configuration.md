# Configuration reference

Kimlik reads standard .NET configuration: `appsettings.json`, then environment variables. In an environment variable, `__` separates the levels, so `Kimlik:Server:PublicUrl` becomes `Kimlik__Server__PublicUrl`. Durations are written as `[d.]hh:mm:ss`, such as `00:10:00` for ten minutes or `14.00:00:00` for fourteen days.

Kimlik checks its settings at startup and refuses to start with an invalid one, naming the setting in the error.

## Required

| Setting | Description |
|---|---|
| `ConnectionStrings:Kimlik` | The PostgreSQL connection string, such as `Host=db;Database=kimlik;Username=kimlik;Password=…`. PostgreSQL 16 or later. |
| `Kimlik:Server:PublicUrl` | The address people and applications reach Kimlik at, such as `https://id.example.com/`. It is the token issuer and the base of every link in emails, so it is never taken from request headers. |
| `Kimlik:Security:MasterKey` | 256 bits in base64 (`openssl rand -base64 32`). It encrypts what Kimlik keeps secret but must read back: token signing keys, the Data Protection key ring, authenticator secrets and webhook secrets. **Back it up**: without it, those cannot be read, and every user must set up two-factor authentication again. |

## Server

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Server:RequireHttps` | `true` | Refuses plain HTTP. Turn it off only for local development; behind a proxy that terminates TLS, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` instead. |
| `Kimlik:Admin:Enabled` | `true` | Whether this instance serves the admin panel under `/admin`. |
| `Kimlik:Database:MigrateOnStartup` | `true` | Applies migrations, the bootstrap administrator and the provisioning file at startup. Turn it off to run `kimlik migrate` as a deployment step instead. |
| `Kimlik:Provisioning:FilePath` | | A provisioning file to apply whenever the database is prepared. See the README. |

## Bootstrap

| Setting | Description |
|---|---|
| `Kimlik:Bootstrap:AdminEmail` | The first administrator, created while the installation has no administrator. An existing account with this address is promoted only if it has verified the address. |
| `Kimlik:Bootstrap:AdminPassword` | Their password. Set both or neither, and remove them once the administrator has signed in. |

## Accounts

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Accounts:Registration` | `Open` | `Open` lets anyone create an account; `InviteOnly` leaves it to administrators and invitations; `Disabled` turns sign-up off. |
| `Kimlik:Accounts:RequireVerifiedEmail` | `true` | People confirm their email address before they can sign in. |
| `Kimlik:Accounts:PasswordMinimumLength` | `12` | Between 8 and 128. Kimlik has no composition rules: length makes passwords strong (NIST SP 800-63B). |
| `Kimlik:Accounts:MaxFailedSignInAttempts` | `5` | Wrong passwords or codes in a row before the account is locked. |
| `Kimlik:Accounts:LockoutDuration` | `00:15:00` | How long a locked account stays locked. |
| `Kimlik:Accounts:SessionLifetime` | `14.00:00:00` | How long a "keep me signed in" session lasts without activity. |

## Two-factor authentication

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Mfa:RequireForAdministrators` | `true` | Accounts that hold a system permission must use a second factor; they set one up at their next sign-in, and the admin panel only accepts sessions verified with one. |
| `Kimlik:Mfa:RequireForEveryone` | `false` | Every account must use a second factor. Organizations can also require one of their members. |
| `Kimlik:Mfa:RememberBrowserFor` | `30.00:00:00` | How long a browser that the user trusts skips the second factor; `00:00:00` turns trusted browsers off. |
| `Kimlik:Mfa:RememberBrowserForAdministrators` | `false` | Whether administrators can trust a browser too. |

## Tokens

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Tokens:AccessTokenLifetime` | `00:10:00` | |
| `Kimlik:Tokens:IdentityTokenLifetime` | `00:10:00` | |
| `Kimlik:Tokens:AuthorizationCodeLifetime` | `00:05:00` | |
| `Kimlik:Tokens:RefreshTokenLifetime` | `14.00:00:00` | Sliding: each use of a refresh token issues one with this lifetime. |
| `Kimlik:Tokens:RefreshTokenAbsoluteLifetime` | `90.00:00:00` | However often it is used, a session ends after this long. |
| `Kimlik:Tokens:RefreshTokenReuseLeeway` | `00:00:30` | A refresh token used again within this time, as by a retrying client, is not taken for theft. |
| `Kimlik:Tokens:Keys:RotationInterval` | `90.00:00:00` | How often signing and encryption keys are replaced. |
| `Kimlik:Tokens:Keys:PrepublishPeriod` | `2.00:00:00` | How long a new key is published before it signs, so that resource servers have it cached in time. |
| `Kimlik:Tokens:Keys:RetentionPeriod` | `90.00:00:00` | How long a replaced key keeps validating; it must cover the absolute refresh token lifetime. |

## Organizations, plans and audit

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Organizations:UsersCanCreate` | `true` | Whether any user can create an organization through the Account API. |
| `Kimlik:Organizations:CreatorRoles` | `["kimlik-org-admin"]` | The roles the creator of an organization gets in it. |
| `Kimlik:Organizations:InvitationLifetime` | `7.00:00:00` | |
| `Kimlik:Plans:DefaultUserPlan` | | The plan of users without a current subscription, by key. |
| `Kimlik:Plans:DefaultOrganizationPlan` | | The plan of organizations without a current subscription. |
| `Kimlik:Plans:ExpirationInterval` | `00:05:00` | How often ended subscriptions are marked expired. Entitlements never wait for it. |
| `Kimlik:Audit:RetentionPeriod` | `365.00:00:00` | How long audit events are kept. |

## Email

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Email:FromAddress` | | The sender of verification, password reset and invitation emails. Without it and an SMTP host, emails wait in the outbox, with a warning in the log, and go out once email is configured. |
| `Kimlik:Email:FromName` | the product name | |
| `Kimlik:Email:Smtp:Host` | | |
| `Kimlik:Email:Smtp:Port` | `587` | |
| `Kimlik:Email:Smtp:Username`, `Kimlik:Email:Smtp:Password` | | |
| `Kimlik:Email:Smtp:Security` | `Auto` | `Auto`, `StartTls`, `SslOnConnect` or `None`. |

## Branding

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Branding:ProductName` | `Kimlik` | Shown on the hosted pages, in emails and in authenticator apps. |
| `Kimlik:Branding:LogoUrl` | | An image shown at the top of the hosted pages instead of the product name. |
| `Kimlik:Branding:PrimaryColor` | `#4f46e5` | The accent color of the hosted pages and emails. |

## Sign-in with other providers

A provider is offered on the sign-in page once its client ID is set. Register `{PublicUrl}/signin/external/callback/{provider}` with it as the redirect URI, where `{provider}` is `google`, `microsoft`, `apple` or `github`.

| Setting | Description |
|---|---|
| `Kimlik:SocialLogin:Google:ClientId`, `…:ClientSecret` | An OAuth client of type "Web application" in the Google Cloud console. |
| `Kimlik:SocialLogin:Microsoft:ClientId`, `…:ClientSecret` | An app registration in Microsoft Entra, with a "Web" redirect URI and a client secret. |
| `Kimlik:SocialLogin:Microsoft:Tenant` | `common` (default) for work, school and personal accounts; `organizations`, `consumers` or one tenant's ID to narrow it. |
| `Kimlik:SocialLogin:GitHub:ClientId`, `…:ClientSecret` | An OAuth app in GitHub's developer settings. |
| `Kimlik:SocialLogin:Apple:ClientId` | The Services ID set up for Sign in with Apple. Apple requires HTTPS, also in development. |
| `Kimlik:SocialLogin:Apple:TeamId`, `…:KeyId`, `…:PrivateKey` | The team ID, and the ID and PEM text of a key with Sign in with Apple enabled; Kimlik signs its client secret with it. |
| `Kimlik:SocialLogin:{Provider}:TrustEmail` | Whether an address the provider verified counts as verified in Kimlik. On for Google, Apple and GitHub; off for Microsoft, which does not verify every address. |

## Webhooks and the outbox

| Setting | Default | Description |
|---|---|---|
| `Kimlik:Webhooks:Timeout` | `00:00:10` | How long an endpoint has to answer. |
| `Kimlik:Webhooks:RetryDelays` | about 21 hours | The waits before each retry, as a list: `Kimlik__Webhooks__RetryDelays__0=00:00:10`, `…__1=00:01:00` and so on. The delivery fails once they are used up. |
| `Kimlik:Webhooks:RetentionPeriod` | `30.00:00:00` | How long the delivery log is kept. |
| `Kimlik:Webhooks:PollingInterval`, `Kimlik:Webhooks:BatchSize` | `00:00:05`, `20` | How often other instances' deliveries and retries are looked for, and how many at a time. |
| `Kimlik:Outbox:PollingInterval`, `Kimlik:Outbox:BatchSize` | `00:00:05`, `20` | The same for emails and other side effects. |
| `Kimlik:Outbox:MaxAttempts` | `10` | |
| `Kimlik:Outbox:RetentionPeriod` | `7.00:00:00` | How long delivered messages are kept. |

## Rate limits

Per client address (IPv6 by /64), per minute.

| Setting | Default | Applies to |
|---|---|---|
| `Kimlik:RateLimits:SignInsPerMinute` | `10` | Passwords and second-factor codes on the hosted pages, and passwords and codes confirmed on the account pages |
| `Kimlik:RateLimits:SignUpsPerMinute` | `10` | Sign-ups |
| `Kimlik:RateLimits:EmailRequestsPerMinute` | `5` | Requests for verification and password reset emails |
| `Kimlik:RateLimits:ProtocolRequestsPerMinute` | `300` | The token, introspection and revocation endpoints |

## Observability

| Setting | Description |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Exports traces, metrics and logs over OTLP, such as to an OpenTelemetry Collector. Without it, nothing is exported. The other standard `OTEL_*` variables apply too. |
| `Logging:LogLevel:*` | The usual .NET log levels. |
