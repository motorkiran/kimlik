import { beforeEach, describe, expect, it } from "vitest";
import { FakeAccountApi, mount, newId, organization, query, shows, submit } from "./fakeAccountApi.js";

describe("<kimlik-organizations>", () => {
  let api: FakeAccountApi;

  beforeEach(() => {
    api = new FakeAccountApi().configure();
    api.organizations = [organization("Acme", "acme", [], ["kimlik-org-admin"])];
  });

  it("lists the user's organizations and tells the page which one they choose", async () => {
    const element = await mount("kimlik-organizations", {}, "Acme");
    let chosen: unknown;
    document.addEventListener("kimlik-organization-select", (event) => (chosen = (event as CustomEvent).detail), { once: true });

    query(element, "button[part=organization]").click();

    expect(chosen).toEqual({ id: api.organizations[0]!.id, slug: "acme" });
    expect(element.shadowRoot!.textContent).toContain("Your roles: kimlik-org-admin");
    expect(api.requests[0]!.authorization).toMatch(/^Bearer /);
    expect(api.requests.every((request) => request.method === "GET")).toBe(true);
  });

  it("accepts invitations, after which the organization is listed", async () => {
    const invitation = { id: newId(), organizationId: newId(), organizationName: "Globex", roles: [], expiresAt: "2026-10-20T10:00:00Z" };
    api.invitationsToMe = [invitation];
    const element = await mount("kimlik-organizations", {}, "Globex invites you to join.");

    query(element, `[data-key="accept-${invitation.id}"]`).click();

    await shows(element, "You joined Globex.");
    expect(api.changes()).toEqual([`POST /invitations/${invitation.id}/accept`]);
    expect(element.shadowRoot!.textContent).not.toContain("invites you");
    expect(query(element, "[role=status]").textContent).toBe("You joined Globex.");
  });

  it("creates organizations only where the page allows it", async () => {
    const closed = await mount("kimlik-organizations", {}, "Acme");
    expect(closed.shadowRoot!.querySelector("form")).toBeNull();

    const open = await mount("kimlik-organizations", { "allow-create": "" }, "Create an organization");
    query<HTMLInputElement>(open, "input[name=name]").value = "Initech";
    query<HTMLInputElement>(open, "input[name=slug]").value = "initech";
    submit(query<HTMLFormElement>(open, "form"));

    await shows(open, "Initech is created.");
    expect(api.requests.find((request) => request.method === "POST")?.body).toEqual({ name: "Initech", slug: "initech" });
  });

  it("shows why Kimlik refused, in the page's language", async () => {
    api.refuseNext = { status: 403, code: "organization.creation_disabled" };
    document.documentElement.lang = "tr";
    try {
      const element = await mount("kimlik-organizations", { "allow-create": "" }, "Organizasyonlarınız");
      query<HTMLInputElement>(element, "input[name=name]").value = "Initech";
      query<HTMLInputElement>(element, "input[name=slug]").value = "initech";
      submit(query<HTMLFormElement>(element, "form"));

      await shows(element, "Burada organizasyonları yalnızca yöneticiler oluşturabilir.");
      expect(query(element, "[role=alert]").textContent).toBe("Burada organizasyonları yalnızca yöneticiler oluşturabilir.");
    } finally {
      document.documentElement.lang = "";
    }
  });
});
