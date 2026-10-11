# @kimlik/node

Protects Node.js APIs with [Kimlik](https://github.com/motorkiran/kimlik), an open-source, self-hosted identity and access management server, and calls its Management API.

- Validates Kimlik access tokens for your API's audience, and optionally Kimlik API keys (`Authorization: Bearer kmk_…`).
- Gives you the caller as a `KimlikUser`: user or service client, permissions, organization and plan, and the administrator acting as the user, if one is.
- Answers 401 and 403 for you with `kimlikMiddleware` and `requirePermission`, for Express, Connect and other `(req, res, next)` frameworks.
- Reads a caller's features and limits with `entitlementsOf`.
- Calls the Management API through `kimlik.client`, typed from Kimlik's OpenAPI document, with client credentials tokens it gets and renews by itself.
- Checks the signatures of Kimlik's webhooks (`verifyWebhook`), as the Standard Webhooks specification describes.

Node.js 20 or later; the package is ES modules with its types.

```js
import { createKimlik, kimlikMiddleware, requirePermission } from "@kimlik/node";

const kimlik = createKimlik({ authority: "https://id.example.com/", audience: "invoices-api" });

app.get("/invoices", kimlikMiddleware(kimlik), requirePermission("invoices:read"), (req, res) => {
  res.json(listInvoices(req.kimlik.organizationId));
});
```

API keys, entitlements and the Management API need your API's service client, as `clientId` and `clientSecret`, whose role holds what they call for: `kimlik.api_keys:verify` for API keys and `kimlik.subscriptions:read` for entitlements. API key answers and entitlements are cached for a minute.

## Development

```sh
npm ci
npm run generate   # the Management API types, from tests/Kimlik.Server.Tests/Api/openapi-v1.json
npm test
npm run build
```
