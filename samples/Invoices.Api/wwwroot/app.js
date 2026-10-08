// The sample's settings come from the API, so the Kimlik address lives in one place.
const config = await (await fetch("/config.json")).json();

const userManager = new oidc.UserManager({
  authority: config.authority,
  client_id: config.clientId,
  redirect_uri: `${location.origin}/`,
  post_logout_redirect_uri: `${location.origin}/`,
  response_type: "code",
  scope: "openid profile email offline_access invoices",
});

const $ = (id) => document.getElementById(id);

function show(message) {
  $("status").textContent = message;
}

// Kimlik redirects back here with ?code=…&state=… after sign-in.
if (new URLSearchParams(location.search).has("code")) {
  await userManager.signinRedirectCallback();
  history.replaceState(null, "", "/");
}

async function api(path, options = {}) {
  const user = await userManager.getUser();
  return fetch(path, { ...options, headers: { ...options.headers, Authorization: `Bearer ${user.access_token}` } });
}

async function render() {
  const user = await userManager.getUser();
  const signedIn = user !== null && !user.expired;

  $("sign-in").hidden = signedIn;
  $("sign-out").hidden = !signedIn;
  $("signed-in").hidden = !signedIn;
  $("signed-out").hidden = signedIn;
  $("greeting").textContent = signedIn ? `Signed in as ${user.profile.name ?? user.profile.email}` : "";

  if (signedIn) {
    // The API reads roles and permissions from the access token.
    const me = await (await api("/api/me")).json();
    $("roles").textContent = me.roles.join(", ") || "none";
    $("permissions").textContent = me.permissions.join(", ") || "none";
  }
}

async function loadInvoices() {
  const response = await api("/api/invoices");
  if (response.status === 403) {
    show("The API refused: your token does not carry invoices:read.");
    return;
  }

  const rows = (await response.json()).map((invoice) => {
    const row = document.createElement("tr");
    for (const value of [invoice.customer, invoice.amount.toFixed(2), invoice.createdBy]) {
      row.insertCell().textContent = value;
    }
    return row;
  });

  $("invoices").replaceChildren(...rows);
  show(`${rows.length} invoices.`);
}

$("sign-in").addEventListener("click", () => userManager.signinRedirect());
$("sign-out").addEventListener("click", () => userManager.signoutRedirect());
$("load-invoices").addEventListener("click", loadInvoices);

$("become-accountant").addEventListener("click", async () => {
  const response = await api("/api/demo/become-accountant", { method: "POST" });
  if (!response.ok) {
    show((await response.json()).detail ?? "The API could not change your roles.");
    return;
  }

  // Permissions are fixed in a token; the refresh token gets a new one that carries the new role.
  await userManager.signinSilent();
  await render();
  show("You are an accountant now. Load the invoices again.");
});

$("new-invoice").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.target);
  const response = await api("/api/invoices", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ customer: form.get("customer"), amount: Number(form.get("amount")) }),
  });

  if (response.status === 403) {
    show("The API refused: your token does not carry invoices:write.");
    return;
  }

  event.target.reset();
  await loadInvoices();
});

await render();
