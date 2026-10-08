# Samples

## Invoices

A single-page app and its API, protected by Kimlik:

- The app (`Invoices.Api/wwwroot`) signs users in with the authorization code flow and PKCE through [oidc-client-ts](https://github.com/authts/oidc-client-ts), and calls the API with their access token.
- The API (`Invoices.Api`) accepts Kimlik access tokens with `Kimlik.AspNetCore` and authorizes by permission: `invoices:read` lists invoices and `invoices:write` creates them.
- The API also accepts Kimlik API keys (`Authorization: Bearer kmk_…`), which users and organizations create with the Account API (`POST /api/v1/me/api-keys`). A key carries the permissions it was given, as long as its owner still holds them, and the owner's plan.
- The plan decides the rest: the Free plan allows three invoices per user and no export; Pro allows unlimited invoices and export. The API gates export with `RequireFeature("export_pdf")` and reads the `max_invoices` limit through `IKimlikEntitlements`.
- Demo buttons make the signed-in user an accountant and upgrade them to Pro. The API does both through the Management API with `Kimlik.Client`, as its own service client, much as a billing integration would; the app then refreshes its token to get the new permissions and plan.

[`provisioning.json`](provisioning.json) declares the permissions, roles, features, plans, API resource and clients the sample needs.

### Run it

```bash
docker compose up -d                                                   # PostgreSQL and Mailpit
dotnet run --project src/Kimlik.Server --launch-profile samples         # Kimlik on http://localhost:5080
dotnet run --project samples/Invoices.Api                               # the sample on http://localhost:5173
```

The `samples` launch profile applies the provisioning file, makes Free the default plan of users and supplies the secret of the sample's service client.

Open http://localhost:5173, choose **Sign in** and create an account; the verification email arrives in Mailpit at http://localhost:8025. Signed in, **Load invoices** is refused until **Become an accountant (demo)** gives you the role. On the Free plan the fourth invoice and **Export** are refused until **Upgrade to Pro (demo)**.

The development administrator, `admin@kimlik.localhost`, cannot use the demo button: Kimlik does not let the sample's service client, which may only manage users, change the roles of an account with more access than its own.

### In your own apps

- Reference the `Kimlik.AspNetCore` and `Kimlik.Client` packages instead of the projects.
- Kimlik accepts browser calls to its token endpoint only from the origins of registered redirect URIs, so register the app's exact URLs.
- Browser apps keep tokens in session storage here. For sensitive data, consider a backend-for-frontend that keeps tokens on the server.
