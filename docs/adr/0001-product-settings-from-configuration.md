# 1. Product settings come from configuration

- **Status:** Accepted
- **Date:** 2026-10-08

## Context

The design kept product settings (branding, registration mode, password and MFA policies, token lifetimes and default plans) in a `settings` table. Administrators would change them in the admin panel, through a `/settings` Management API endpoint or in the provisioning file.

Up to M7 they were built as validated options instead, read from standard .NET configuration like the infrastructure settings: `Kimlik:Branding`, `Kimlik:Accounts`, `Kimlik:Mfa`, `Kimlik:Tokens` and the others in [configuration.md](../configuration.md). The admin panel covers every other management task.

## Decision

Version 0.1.0 reads product settings from configuration only. Keeping them in the database and changing them at runtime is a later phase (design §4.2).

## Consequences

- Changing a setting means changing the configuration and restarting. Every instance needs the same values, which deployments that keep configuration in files or environment variables already ensure.
- Settings are versioned and reviewed with the deployment, and invalid values stop Kimlik at startup instead of reaching users.
- Version 0.1.0 has no `settings` table, `/settings` endpoint, `kimlik.settings:write` permission or `settings.updated` audit event, and administrators cannot change settings in the admin panel.
- When settings move to the database, the options classes can stay what the code reads, with configuration as the defaults.
