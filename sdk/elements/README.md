# @kimlik/elements

Web components that put organization management from [Kimlik](https://github.com/motorkiran/kimlik), an open-source, self-hosted identity and access management server, inside your own pages. They are custom elements, so they work with any framework or none, and they have no runtime dependencies.

- `<kimlik-organizations>` lists the user's organizations and the invitations to them, which the user accepts or declines. With `allow-create`, users also create organizations, where your installation lets them.
- `<kimlik-organization-members organization="…">` shows the members and pending invitations. Users who may manage members invite people, send invitations again, revoke them, change members' roles and remove members.
- `<kimlik-organization-settings organization="…">` renames the organization and changes its short name, logo and MFA requirement. It deletes the organization once its short name is typed. Every member can leave there, after confirming.

Each component shows only what the user's roles in the organization allow, and Kimlik checks every change again.

## Setting up

The components call Kimlik's Account API from the browser with the user's access token for the `kimlik.organizations` scope. That scope reaches the organization endpoints and nothing else.

1. Allow your app's client the `kimlik.organizations` scope in Kimlik. Kimlik then answers cross-origin calls from the origins of its redirect URIs.
2. Ask for a token with the scope, and give the components a function that returns a current one. Ask for it on its own, not together with your API's scope: a token for both could be used on the user's organizations by whoever receives it at your API. The first time, Kimlik asks the user to allow your app to manage their organizations.

```js
import { configureKimlik } from "@kimlik/elements";

configureKimlik({
  authority: "https://id.example.com/",
  accessToken: async () => (await userManager.getUser()).access_token, // oidc-client-ts, for example
});
```

```html
<kimlik-organizations allow-create></kimlik-organizations>
<kimlik-organization-members organization="0199c3a1-1111-7d3c-8b4a-5e6f70819203"></kimlik-organization-members>
<kimlik-organization-settings organization="0199c3a1-1111-7d3c-8b4a-5e6f70819203"></kimlik-organization-settings>
```

```js
document.addEventListener("kimlik-organization-select", (event) => router.navigate(`/orgs/${event.detail.slug}`));
```

The components tell the page what happened through events, which bubble out of them:

| Event | When | `detail` |
| --- | --- | --- |
| `kimlik-organization-select` | The user chose an organization in the list | `id`, `slug` |
| `kimlik-organization-create` | The user created one | `id`, `slug` |
| `kimlik-organization-join` | The user accepted an invitation | `id` |
| `kimlik-organization-update` | The settings were saved | `id`, `slug` (which may be new) |
| `kimlik-organization-leave` | The user left the organization | `id` |
| `kimlik-organization-delete` | The user deleted it | `id` |

Importing the package defines the elements in a browser and does nothing on a server, so a page rendered on the server can import it; the elements fill in once the page runs in the browser. Call `element.refresh()` to load a component again after something changed elsewhere.

It is not on npm yet; until it is, install the tarball attached to each [release](https://github.com/motorkiran/kimlik/releases).

## Look and language

The components render in a shadow root and take the page's font and colors. These custom properties change the rest:

| Property | Default |
| --- | --- |
| `--kimlik-color-primary` | `#4f46e5` |
| `--kimlik-color-danger` | `#b91c1c` |
| `--kimlik-color-muted` | the text color, lighter |
| `--kimlik-color-border` | the text color, much lighter |
| `--kimlik-radius` | `6px` |

Their main parts can be styled with `::part()`: `organizations`, `organization`, `invitations`, `create`, `members`, `member`, `invite`, `invitation`, `settings`, `leave`, `delete`, `error` and `status`.

Text is in English or Turkish, from the closest `lang` attribute, also one outside a shadow root the component is in. Dates follow that language's region.

## Development

```sh
npm ci
npm run generate   # the Account API types, from tests/Kimlik.Server.Tests/Api/openapi-v1.json
npm test
npm run build
```
