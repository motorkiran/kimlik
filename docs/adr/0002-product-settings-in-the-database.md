# 2. Product settings can be changed at runtime

- **Status:** Accepted
- **Date:** 2026-10-11
- **Supersedes:** [ADR 0001](0001-product-settings-from-configuration.md)

## Context

[ADR 0001](0001-product-settings-from-configuration.md) kept product settings in configuration, so that changing one meant changing the deployment and restarting. That suits operators who keep configuration in version control, but people who run Kimlik for a product often want to open registration, require MFA or change the product's name from the admin panel. A setting kept in the database has to win over the configuration files, yet an operator must still be able to pin a value that nobody can change from the inside.

## Decision

- Product settings from a curated catalog are stored in a `settings` table and read through a configuration provider. The provider sits after the configuration files and before environment variables and the command line. Values in the database therefore override `appsettings.json`, while values set in the environment cannot be changed at runtime and show as set by the deployment.
- The options classes stay what the code reads. Code that reads an editable setting reads it through `IOptionsMonitor<T>`, or through Identity's options, which follow the account settings. Changes therefore apply to the next request.
- The catalog covers settings that can change safely while Kimlik runs:
  - branding (product name, logo and color)
  - registration mode, verified email, email codes, breached-password checks and minimum password length
  - MFA requirements for administrators and for everyone
  - whether users can create organizations, and how long invitations last
  - default plans
  - the CAPTCHA provider and its keys
  - the social providers: their client IDs and secrets, Apple's key, the Microsoft tenant and whether their addresses are trusted. OpenIddict's client builds its registrations again when they change.
- Settings that wire up infrastructure at startup stay in configuration only: connection strings, keys, token lifetimes, rate limits, email and text messages.
- Changes are validated as a whole with the same validators as at startup before they are saved, so an invalid value never takes effect. Secrets, such as the CAPTCHA secret key, are encrypted with the master key, and the API never returns them.
- Each instance loads the settings after migrating the database, applies its own changes at once, and checks every 15 seconds whether another instance changed them.
- Reading settings takes `kimlik.settings:read`. Changing them takes `kimlik.settings:write` and every installation-wide system permission, since they include security policies such as MFA for administrators. Changes are audited as `settings.updated`.

## Consequences

- Administrators change these settings in the admin panel or through `/api/v1/settings`, without a restart. Other instances follow within 15 seconds.
- Operators who want a setting fixed set it in the environment. Settings files remain the defaults.
- The provisioning file does not carry settings. Configuration as code keeps using configuration.
- Text messages, email and the other infrastructure settings still take a restart.
