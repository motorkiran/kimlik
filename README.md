# Kimlik

Kimlik ("identity" in Turkish) is an open-source, self-hosted identity and access management server. It is being built as a standards-compliant OpenID Connect provider with users, organizations, roles and permissions, plans and entitlements, multi-factor authentication, API keys and webhooks.

> **Status:** early development. The foundation (milestone M0) is in place; identity features arrive from milestone M1 on. Not ready for production use.

The [design document](docs/design.md) describes the vision, scope, architecture and roadmap.

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
| http://localhost:8025 | Mailpit inbox for development email |

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

### Database migrations

Migrations run at startup by default. To run them as a separate deployment step instead, set `Kimlik__Database__MigrateOnStartup=false` and run:

```bash
dotnet run --project src/Kimlik.Server -- migrate
```

## License

Kimlik is licensed under the [Apache License 2.0](LICENSE).
