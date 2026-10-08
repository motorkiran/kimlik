# Changelog

All notable changes to Kimlik are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/); until 1.0, minor versions may break compatibility.

## [0.1.0] - Unreleased

The first release: a self-hosted identity and access management server with a .NET SDK.

### Identity and sign-in

- An OpenID Connect provider (OpenIddict): authorization code with PKCE for every client, refresh tokens with rotation and reuse detection, client credentials, discovery, JWKS, user info, end session, introspection and revocation. Signing and encryption keys rotate on their own and are shared by every instance.
- Hosted pages for sign-up, sign-in, email verification, password reset, consent and organization selection, in English and Turkish, with branding, a strict content security policy and rate limits.
- Two-factor authentication with authenticator apps and recovery codes, required for administrators by default, per organization or for everyone; trusted browsers; step-up for sessions that need it; `amr` in tokens.
- Sign-in with Google, Microsoft, Apple and GitHub, without ever linking accounts on an email address alone.
- Account pages for the profile, password, two-factor authentication, connected accounts, signed-in applications and account deletion.

### Access and multi-tenancy

- Permissions and roles, global or per organization, carried in access tokens; system permissions guard Kimlik itself, and nobody can grant access they do not hold.
- Organizations with members, roles and email invitations, and an organization context in tokens.
- Features, plans and subscriptions, with a `plan` claim and an entitlements API.
- API keys for users and organizations, verified by resource servers.

### APIs, integration and operations

- A Management API and an Account API (`/api/v1`) with an OpenAPI 3.1 document.
- Signed webhooks (Standard Webhooks) for user, organization, membership, invitation, subscription and API key events, delivered from an outbox with retries.
- A provisioning file that declares permissions, roles, features, plans, API resources and clients as code.
- An admin panel under `/admin` (Blazor and MudBlazor) for every management task.
- `Kimlik.AspNetCore` for resource servers (tokens and API keys, `RequirePermission`, `RequireFeature`, `KimlikUser`, webhook verification) and `Kimlik.Client` for the Management API.
- An audit log with retention, OpenTelemetry, health checks, and a container image that runs as non-root.
