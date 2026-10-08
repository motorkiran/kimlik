# Deploying Kimlik

Kimlik is one ASP.NET Core application and a PostgreSQL database. This guide covers what a production installation needs; [configuration.md](configuration.md) lists every setting.

## What you need

- PostgreSQL 16 or later, with a database and a user that owns it. Kimlik creates its tables in the `kimlik` schema.
- A container runtime, or the .NET 10 runtime to run the published application.
- A domain with TLS, such as `https://id.example.com`, usually terminated by a reverse proxy or load balancer.
- An SMTP relay for verification, password reset and invitation emails.
- A 256-bit master key: `openssl rand -base64 32`. Store it in your secret manager and back it up with the database.

## Build the image

```bash
dotnet publish src/Kimlik.Server -c Release -t:PublishContainer
```

This builds `kimlik:latest` on a chiseled Ubuntu image that runs as a non-root user and listens on port 8080. Push it to your registry, or use `-p:ContainerRegistry=…` to publish straight to one.

## Run it

```bash
docker run -d --name kimlik -p 8080:8080 \
  -e ConnectionStrings__Kimlik="Host=db;Database=kimlik;Username=kimlik;Password=…" \
  -e Kimlik__Server__PublicUrl=https://id.example.com/ \
  -e Kimlik__Security__MasterKey=… \
  -e Kimlik__Email__FromAddress=no-reply@example.com \
  -e Kimlik__Email__Smtp__Host=smtp.example.com \
  -e Kimlik__Email__Smtp__Username=… -e Kimlik__Email__Smtp__Password=… \
  -e Kimlik__Bootstrap__AdminEmail=you@example.com \
  -e Kimlik__Bootstrap__AdminPassword=… \
  -e ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  kimlik:latest
```

On the first start Kimlik creates its tables, its signing and encryption keys and the bootstrap administrator. Sign in at `https://id.example.com/admin`: administrators set up two-factor authentication first. Then remove the bootstrap password from the configuration.

## Behind a reverse proxy

Terminate TLS at the proxy and forward to port 8080. With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, Kimlik takes the client address and scheme from the `X-Forwarded-For` and `X-Forwarded-Proto` headers; only let your proxy set them. Rate limits, the audit log and the secure cookies depend on them.

The proxy must pass WebSocket connections on `/_blazor` through, for the admin panel.

`Kimlik:Server:PublicUrl` must be the address people see. Kimlik never derives it from request headers, so a wrong `Host` header cannot change the token issuer or the links in emails. Passkeys belong to its host too: moving Kimlik to another host leaves them unusable, and people sign in another way to add new ones.

## Database migrations

By default Kimlik migrates the database at startup, under a PostgreSQL advisory lock, so several instances can start at once. To migrate as a separate deployment step instead, set `Kimlik__Database__MigrateOnStartup=false` and run the image with the `migrate` argument before rolling out:

```bash
docker run --rm -e ConnectionStrings__Kimlik=… -e Kimlik__Security__MasterKey=… -e Kimlik__Server__PublicUrl=… kimlik:latest migrate
```

## Several instances

Instances share everything through PostgreSQL: Data Protection keys, token keys, the outbox and webhook deliveries. Background work (key rotation, subscription expiry, token pruning, audit retention, email and webhook delivery) is safe to run on every instance; locks make sure each job runs once.

The admin panel keeps a live connection per browser (Blazor Server), so a load balancer in front of several instances needs sticky sessions for `/admin` and `/_blazor`. Alternatively, serve the panel from one internal instance and set `Kimlik__Admin__Enabled=false` on the public ones.

Rate limits are kept per instance, so with *n* instances a client can make up to *n* times the configured requests.

## Health and monitoring

- `GET /health/live` answers while the process runs.
- `GET /health/ready` answers once the database is reachable and the token keys are loaded; route traffic only to ready instances.
- Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export traces, metrics and logs to an OpenTelemetry Collector.

## Backups and recovery

Back up the PostgreSQL database, and keep the master key safe but apart from the backups. A restored database needs the same master key: without it, the token keys, the Data Protection key ring, authenticator secrets and webhook secrets cannot be decrypted, and the installation does not become ready.

## Hardening checklist

- [ ] TLS everywhere, and `Kimlik:Server:RequireHttps` left on.
- [ ] The master key from a secret manager, not from a file in the image.
- [ ] Registration set to `InviteOnly` or `Disabled` unless anyone may sign up.
- [ ] The bootstrap password removed after the first sign-in.
- [ ] The admin panel reachable only from where administrators work, or served from an internal instance.
- [ ] Email delivery configured and tested, so that verification and password resets work.
- [ ] Database backups, restore tested, with the master key kept apart.
- [ ] OTLP export, or at least the logs, collected somewhere you look.
