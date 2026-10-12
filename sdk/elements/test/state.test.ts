import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Member, MyOrganization } from "../src/api.js";
import { configureKimlik } from "../src/index.js";
import { authority, FakeAccountApi, me, mount, newId, organization, pendingInvitation, query, shows, submit } from "./fakeAccountApi.js";

const read = "kimlik.org.members:read";
const write = "kimlik.org.members:write";

function member(name: string, roles: string[] = ["viewer"]): Member {
  return { userId: newId(), email: `${name.toLowerCase()}@example.com`, name, roles, joinedAt: "2026-10-01T10:00:00Z" };
}

/** How the components keep what they show, send and say in step with what the user does. */
describe("the components' state", () => {
  let api: FakeAccountApi;
  let acme: MyOrganization;
  let globex: MyOrganization;

  beforeEach(() => {
    api = new FakeAccountApi().configure();
    acme = organization("Acme", "acme", [read, write], ["manager"]);
    globex = organization("Globex", "globex", [read, write], ["manager"]);
    api.organizations = [acme, globex];
    api.roles = [{ key: "viewer", name: "Viewer", description: null, permissions: [] }];
  });

  it("shows the organization asked for last, whichever answer arrives last", async () => {
    const ada = member("Ada");
    const grace = member("Grace");
    let releaseAcme = (): void => undefined;
    const acmeHeld = new Promise<void>((resolve) => (releaseAcme = resolve));
    api.intercept = async (method, path) => {
      if (method !== "GET" || !path.endsWith("/members")) {
        return undefined;
      }

      if (path.includes(acme.id)) {
        await acmeHeld;
      }

      return Response.json({ items: [path.includes(acme.id) ? ada : grace], nextCursor: null });
    };
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Loading…");

    element.setAttribute("organization", globex.id);
    await shows(element, "Grace");
    releaseAcme();
    await vi.waitFor(() => expect(api.requests.filter((request) => request.path.endsWith("/members"))).toHaveLength(2));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(element.shadowRoot!.textContent).not.toContain("Ada");
    query(element, `[data-key="remove-${grace.userId}"]`).click();
    query(element, `[data-key="confirm-remove-${grace.userId}"]`).click();
    await vi.waitFor(() => expect(api.changes()).toEqual([`DELETE /organizations/${globex.id}/members/${grace.userId}`]));
  });

  it("takes what was open for one organization away as soon as another is asked for", async () => {
    const ada = member("Ada");
    api.members = [ada];
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Ada");
    query(element, `[data-key="remove-${ada.userId}"]`).click();
    expect(element.shadowRoot!.querySelector(`[data-key="confirm-remove-${ada.userId}"]`)).not.toBeNull();

    element.setAttribute("organization", globex.id);

    expect(element.shadowRoot!.querySelector("button")).toBeNull();
    expect(element.shadowRoot!.textContent).toContain("Loading…");
  });

  it("keeps what the user typed when Kimlik refuses it", async () => {
    api.refuseNext = { status: 409, code: "organization.slug_taken" };
    const element = await mount("kimlik-organizations", { "allow-create": "" }, "Create an organization");
    query<HTMLInputElement>(element, "input[name=name]").value = "Initech";
    query<HTMLInputElement>(element, "input[name=slug]").value = "acme";

    submit(query<HTMLFormElement>(element, "form"));

    await shows(element, "Another organization has this short name.");
    expect(query<HTMLInputElement>(element, "input[name=name]").value).toBe("Initech");
    expect(query<HTMLInputElement>(element, "input[name=slug]").value).toBe("acme");
  });

  it("makes one change at a time, and tells the page what came of it", async () => {
    const invitation = { id: newId(), organizationId: newId(), organizationName: "Initech", roles: [], expiresAt: "2026-10-20T10:00:00Z" };
    api.invitationsToMe = [invitation];
    const element = await mount("kimlik-organizations", { "allow-create": "" }, "Initech invites you to join.");
    const events: [string, unknown][] = [];
    for (const type of ["kimlik-organization-join", "kimlik-organization-create"]) {
      document.addEventListener(type, (event) => events.push([type, (event as CustomEvent).detail]), { once: true });
    }

    const accept = query<HTMLButtonElement>(element, `[data-key="accept-${invitation.id}"]`);
    accept.click();
    accept.click();
    expect(accept.disabled).toBe(true);
    await shows(element, "You joined Initech.");
    expect(api.changes()).toEqual([`POST /invitations/${invitation.id}/accept`]);

    query<HTMLInputElement>(element, "input[name=name]").value = "Hooli";
    query<HTMLInputElement>(element, "input[name=slug]").value = "hooli";
    submit(query<HTMLFormElement>(element, "form"));
    await shows(element, "Hooli is created.");

    expect(events).toEqual([
      ["kimlik-organization-join", { id: invitation.organizationId }],
      ["kimlik-organization-create", { id: expect.any(String), slug: "hooli" }],
    ]);
    expect(query<HTMLInputElement>(element, "input[name=name]").value, "a form that went through starts over").toBe("");
  });

  it("announces in regions that stay, and drops an old message when the user moves on", async () => {
    const ada = member("Ada");
    api.members = [ada];
    api.invitations = [pendingInvitation("linus@example.com")];
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "linus@example.com");
    const [status, alert] = [query(element, "[role=status]"), query(element, "[role=alert]")];
    expect(alert.hidden).toBe(true);

    api.refuseNext = { status: 409, code: "invitation.pending" };
    query<HTMLInputElement>(element, "input[name=email]").value = "linus@example.com";
    submit(query<HTMLFormElement>(element, "section[part=invite] form"));
    await shows(element, "That address already has an open invitation.");
    expect(query(element, "[role=alert]")).toBe(alert);
    expect(alert.hidden).toBe(false);

    query(element, `[data-key="edit-${ada.userId}"]`).click();
    expect(alert.hidden).toBe(true);
    expect(alert.textContent).toBe("");
    expect(query<HTMLInputElement>(element, "input[name=email]").value, "the address typed for the invitation stays").toBe("linus@example.com");
    expect(query(element, "[role=status]")).toBe(status);
  });

  it("loads again when Kimlik says the view is out of date", async () => {
    const ada = member("Ada");
    api.members = [ada];
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Ada");
    query(element, `[data-key="remove-${ada.userId}"]`).click();

    // Someone else removed her meanwhile.
    api.members = [];
    api.refuseNext = { status: 404, code: "organization.member_not_found" };
    query(element, `[data-key="confirm-remove-${ada.userId}"]`).click();

    await shows(element, "That person is no longer a member.");
    expect(element.shadowRoot!.querySelector("li[part=member]")).toBeNull();
  });

  it("reads every page of members, and only the invitations still open", async () => {
    api.members = [{ ...member("Me", ["viewer"]), userId: me }, member("Ada"), member("Grace")];
    api.pageSize = 1;
    api.invitations = [pendingInvitation("linus@example.com"), { ...pendingInvitation("old@example.com"), status: "accepted" }];

    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Grace");

    expect(api.requests.filter((request) => request.path.endsWith("/members"))).toHaveLength(3);
    expect(api.requests.find((request) => request.path.endsWith("/invitations"))!.query).toContain("status=pending");
    expect(element.shadowRoot!.textContent).not.toContain("old@example.com");
    expect(element.shadowRoot!.textContent).toContain("Me (you)");
  });

  it("calls Kimlik at the authority's path, and speaks the language of the page around it", async () => {
    configureKimlik({ authority: `${authority}?tenant=1#top`, accessToken: () => "not-a-jwt", fetch: api.fetch });
    const outer = document.createElement("section");
    outer.lang = "tr";
    const host = outer.appendChild(document.createElement("div"));
    const element = host.attachShadow({ mode: "open" }).appendChild(document.createElement("kimlik-organizations"));
    document.body.replaceChildren(outer);

    await shows(element, "Organizasyonlarınız");
    expect(api.requests[0]!.path).toBe("/organizations");
    expect(api.requests[0]!.query).toBe("");
  });
});
