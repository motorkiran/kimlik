# Changelog

All notable changes to Kimlik are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/); until 1.0, minor versions may break compatibility.

## [Unreleased]

### Added

- Phone numbers and SMS codes: texts go out through Netgsm, İleti Merkezi or Twilio (`Kimlik:Sms`); people add a number on the account pages by entering the code texted to it, and sign in with texted codes (`amr` `["sms"]`); the `phone` scope adds `phone_number` and `phone_number_verified`; administrators see and remove numbers through the Management API, `Kimlik.Client` and the admin panel, and users through the Account API.
- Token exchange (RFC 8693): web and service clients allowed to (`allowTokenExchange`) exchange a user's access token meant for them for one to other APIs, acting for the user (`act` names the client, with any earlier actor nested inside); an API registers as a service client whose client ID is its audience.
- Back-channel logout (OpenID Connect Back-Channel Logout 1.0): web clients with a `backChannelLogoutUri` get a signed logout token when the browser session they signed a user in through ends, with its `sid`, which ID tokens now carry, and one without `sid` when all of the user's sessions end, as after signing out everywhere, a password change, a suspension or a deletion.

### Changed

- `UserResponse` and `ProfileResponse` gained `PhoneNumber`, and `ClientResponse` gained `AllowTokenExchange` and `BackChannelLogoutUri`; code that constructs them positionally must pass them.
- The Account API refuses changes from any token in which someone else acts for the user, a client that exchanged it as well as an impersonating administrator.
- Sign-in codes are kept per purpose, so codes asked for before the upgrade stop working.

## [0.2.0] - 2026-10-10

Passwordless and phishing-resistant sign-in, stronger client authentication, and support tools for administrators.

### Added

- Passkeys (WebAuthn): sign in with a passkey, from a button or the browser's suggestions in the address field, as two factors that meet MFA requirements (`amr` `["pop", "mfa"]`); add, rename and remove passkeys on the account pages, with a one-time offer after a password sign-in; manage them through the Account and Management APIs, `Kimlik.Client` and the admin panel.
- Email sign-in codes: a one-time code sent by email replaces the password as a first factor (`amr` `["email"]`), with a required second factor still following; sign-up without a password, and removing a password on the account pages (`Kimlik:Accounts:EmailSignIn`, on by default).
- Personal data export (KVKK article 11, GDPR article 15): people download what Kimlik holds about them from the account pages after a recent sign-in, and administrators export it, with the private metadata, through the Management API, `Kimlik.Client` and the admin panel.
- Breached-password checks: new passwords that appear in known data breaches are refused, through Have I Been Pwned's k-anonymity range API (`Kimlik:Accounts:BreachedPasswordCheck`, on by default).
- The device authorization grant (RFC 8628): native clients, such as command-line tools, sign people in through a code they enter on `/connect/verify`; native clients may now have no redirect URI.
- Admin impersonation: administrators with `kimlik.users:impersonate` sign in as a user for support from the admin panel, for up to 30 minutes, under a banner that lets them stop. The account changes nothing meanwhile, tokens carry `act` (RFC 8693) and no refresh token, `KimlikUser.ActorId` names the administrator, and the audit log records the start, the end and everything in between as theirs.
- Keys instead of client secrets: web and service clients can authenticate with `private_key_jwt` (RFC 7523), from a JWK Set of public keys registered through the Management API, the provisioning file or the admin panel; a new secret replaces the keys. Assertions are typed `client-authentication+jwt`.
- Pushed authorization requests (PAR, RFC 9126) at `/connect/par`, which a client can be set to require (`requirePushedAuthorization`).
- Passkeys as the second step: after a password, an email code or another provider, a passkey verifies the second step instead of an authenticator code, and accounts that must use a second factor and have a passkey are no longer made to set up an app (`amr` `[first factor, "pop", "mfa"]`).
- Sign-in links: sign-in code emails also carry a "Sign in" link that fills the code in, in the browser that asked for it and nowhere else; opening it never signs anyone in by itself, so mail scanners cannot use the code.

### Changed

- `ClientResponse` gained `RequirePushedAuthorization` and `JsonWebKeySet`; code that constructs it positionally, such as test doubles, must pass them.
- After the sign-in that `prompt=login` asks for, the authorization request comes back with a protected `kimlik_login_prompted` parameter instead of without the prompt.
- The `kimlik-admin` role also holds the new `kimlik.users:impersonate` permission.

### Fixed

- `auth_time` and `max_age` use the time the user signed in, which renewing the session cookie no longer moves.
- Webhook deliveries that an instance claimed are handed back when it stops, instead of waiting for their lease to run out.

## [0.1.0] - 2026-10-08

The first release: a self-hosted identity and access management server with a .NET SDK.

### Identity and sign-in

- An OpenID Connect provider (OpenIddict): authorization code with PKCE for every client, refresh tokens with rotation and reuse detection, client credentials, discovery, JWKS, user info, end session, introspection and revocation. Signing and encryption keys rotate on their own and are shared by every instance.
- Hosted pages for sign-up, sign-in, email verification, password reset, consent and organization selection, in English and Turkish, with branding, a strict content security policy and rate limits.
- Two-factor authentication with authenticator apps and recovery codes, required for administrators by default, per organization or for everyone; trusted browsers; step-up for sessions that need it; `amr` in tokens.
- Sign-in with Google, Microsoft, Apple and GitHub, without ever linking accounts on an email address alone.
- Account pages for the profile, password, two-factor authentication, connected accounts, signed-in applications and account deletion.
- Profiles with a name, language, picture and time zone, issued as the standard claims of the `profile` scope.

### Access and multi-tenancy

- Permissions and roles, global or per organization, carried in access tokens; system permissions guard Kimlik itself, and nobody can grant access they do not hold.
- Organizations with members, roles and email invitations, and an organization context in tokens.
- Public and private metadata (JSON) on users and organizations: users and members read the public metadata through the Account API, and only the Management API reads the private metadata or changes either.
- Features, plans and subscriptions, with a `plan` claim and an entitlements API.
- API keys for users and organizations, verified by resource servers.

### APIs, integration and operations

- A Management API and an Account API (`/api/v1`) with an OpenAPI 3.1 document.
- Signed webhooks (Standard Webhooks) for user, organization, membership, invitation, subscription and API key events, delivered from an outbox with retries.
- A provisioning file that declares permissions, roles, features, plans, API resources and clients as code.
- An admin panel under `/admin` (Blazor and MudBlazor) for every management task.
- `Kimlik.AspNetCore` for resource servers (tokens and API keys, `RequirePermission`, `RequireFeature`, `KimlikUser`, webhook verification) and `Kimlik.Client` for the Management API.
- An audit log with retention, OpenTelemetry, health checks, and a container image that runs as non-root.
