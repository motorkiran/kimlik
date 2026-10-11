# Running the OpenID conformance suite

This guide runs the OpenID Foundation's conformance suite against a Kimlik deployment, as a step towards [OpenID certification](https://openid.net/certification/). The design behind it is in [§8.12](design.md#812-openid-certification).

## What is tested

| Test plan | Profile | Notes |
| --- | --- | --- |
| `oidcc-basic-certification-test-plan` | Basic OP | Run it twice: with `client_secret_basic`, then with `client_secret_post`. |
| `oidcc-config-certification-test-plan` | Config OP | Checks the discovery document. |
| `oidcc-rp-initiated-logout-certification-test-plan` | RP-Initiated Logout OP | |
| `oidcc-backchannel-rp-initiated-logout-certification-test-plan` | Back-Channel Logout OP | Kimlik must reach the suite to deliver the logout token. |

For every plan, choose `discovery` as the server metadata, `static_client` as the client registration and `code` as the response type.

Kimlik does not support the implicit and hybrid flows or dynamic client registration, so the Implicit, Hybrid and Dynamic OP plans do not apply.

## Before you start

- Kimlik must be reachable over HTTPS from wherever the suite runs: from the internet for the hosted suite at [certification.openid.net](https://www.certification.openid.net/), or from the suite's own network if you run it yourself. `Kimlik:Server:PublicUrl` must be the address the suite uses.
- The suite's runs sign a test user in again and again. Raise `Kimlik:RateLimits:SignInsPerMinute` for the duration of the runs if the sign-in rate limit gets in the way.

## 1. Set up the clients

[`conformance/provisioning.json`](../conformance/provisioning.json) declares the suite's two clients:

- web apps, first party, with PKCE turned off, because the suite's requests do not carry it;
- the suite's callback, post-logout and back-channel logout addresses.

1. Pick an alias for your test plans, such as `kimlik-yourcompany`; it must be unique on the hosted suite. Replace `kimlik` with it in the file's URLs. If you run the suite yourself, also replace `https://www.certification.openid.net` with your suite's address.
2. Choose two long random secrets, and give them to Kimlik as `Conformance__ClientSecret` and `Conformance__Client2Secret`.
3. Apply the file, for example by setting `Kimlik__Provisioning__FilePath` to it and restarting Kimlik.

## 2. Create the test user

Create a user for the suite to sign in as, in the admin panel or through the Management API:

```sh
curl -X POST https://id.example.com/api/v1/users \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{ "email": "conformance@example.com", "givenName": "Conformance", "familyName": "Tester", "emailVerified": true, "password": "a long password of your own" }'
```

Kimlik keeps only phone numbers verified by text, so the `phone` scope tests show a warning unless the user adds a number on the account pages, which needs texts set up (`Kimlik:Sms`).

## 3. Configure the suite

[`conformance/suite-config.json`](../conformance/suite-config.json) is the configuration to paste into each test plan. Before you paste it, replace:

- `kimlik` in `alias` with your alias;
- `https://id.example.com` with your Kimlik address, everywhere, including the `browser` steps;
- the client secrets with the ones from step 1;
- the test user's address and password with yours from step 2.

The `browser` steps let the suite fill in Kimlik's sign-in page by itself. They also skip the passkey offer, accept the consent page and confirm signing out. If a run stalls on a page, compare these steps with the suite's documentation and with the page, and adjust them.

## 4. Run the plans

On the suite, create a test plan for each row of the table above with the variants given there. Paste the configuration and run every test module.

Some modules ask for a screenshot of the page Kimlik shows instead of redirecting:
- an unregistered `redirect_uri`;
- a missing `response_type`;
- logout requests with an unregistered or changed `post_logout_redirect_uri`, or a missing, changed or foreign `id_token_hint`.

Kimlik shows an error page or asks the user to confirm signing out. Upload a screenshot of it.

These results are expected:

- **Skipped:**
  - request objects and `request_uri`, since discovery does not offer unsigned ones;
  - ID tokens signed with `none`;
  - the `address` scope.
- **Warnings:**
  - `phone_number` is missing until the test user verifies a number;
  - `oidcc-claims-essential` warns that `name` is missing, since Kimlik returns claims by scope, not through the `claims` parameter.

Kimlik's own tests check what these plans check, on every change: `BasicOpConformanceTests` and `LogoutConformanceTests`. If the suite reports a failure they do not, add a test that reproduces it.

## 5. Certification

Once every plan passes, the results can be submitted to the OpenID Foundation for certification, which it charges non-members for. Submitting is the maintainers' decision.
