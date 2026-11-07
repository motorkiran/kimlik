# Contributing to Kimlik

Thanks for taking the time. Bug reports, fixes, documentation and ideas are all welcome.

## Before you start

- **Bugs:** open an issue with what you did, what you expected and what happened, and the version or commit you ran.
- **Security problems:** do not open an issue. Report them privately, as [SECURITY.md](SECURITY.md) describes.
- **New features:** open an issue first. Kimlik records its design in [docs/design.md](docs/design.md) before building it, so a feature starts as a short proposal there: what it does, for whom, and what it leaves out. Small fixes and documentation changes can go straight to a pull request.

## Setting up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the version in `global.json`), a container engine with a Docker-compatible API (Docker Desktop, OrbStack, or Podman 5+ with a rootful machine) and Docker Compose v2. The JavaScript packages need Node.js 20 or later.

```bash
docker compose up -d                       # PostgreSQL and Mailpit
dotnet run --project src/Kimlik.Server     # http://localhost:5080
```

The [README](README.md#local-development) has the details, and the development administrator's credentials.

## Making a change

```bash
dotnet format          # code style, which CI checks
dotnet build           # warnings are errors
dotnet test            # integration tests start PostgreSQL in a container
```

- **Tests.** Every change comes with tests. Data access is tested against PostgreSQL, never an in-memory database. Protocol behavior is tested over HTTP, as a client would see it.
- **API snapshot.** The Management API's OpenAPI document is a snapshot, so changes to it are deliberate. After an intended change, run `KIMLIK_UPDATE_SNAPSHOTS=1 dotnet test`, commit the new snapshot, and regenerate the JavaScript types with `npm run generate` in `sdk/js` and `sdk/elements`.
- **Database.** Schema changes need a migration: `dotnet ef migrations add <Name> --project src/Kimlik.Infrastructure --startup-project src/Kimlik.Server --output-dir Persistence/Migrations` (run `dotnet tool restore` once first).
- **JavaScript packages.** In `sdk/js` or `sdk/elements`: `npm ci`, `npm test`, `npm run typecheck` and `npm run build`.
- **Helm chart.** `charts/kimlik/ci/check.sh` lints and renders the chart.
- **Code.** Follow the conventions in [design §14](docs/design.md#14-engineering-conventions): sealed classes, async with cancellation tokens, time from `TimeProvider`, comments that say why. Code, comments, docs and commit messages are in English.
- **Dependencies.** Only permissive licenses (MIT, Apache-2.0, BSD, PostgreSQL), and say in the pull request why a new one is needed. Libraries that moved to commercial licenses are not used.
- **Changelog.** Add a line under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything users or operators would notice.

## Pull requests

Keep a pull request to one change, with a title that says what it does in the imperative ("Refuse expired invitations"), and a description of why. CI builds, checks formatting, and runs the .NET tests, the JavaScript packages' tests and the Helm chart's checks; it has to be green before a merge.

By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), as the rest of Kimlik is.
