import { beforeEach, describe, expect, it } from "vitest";
import type { MyOrganization } from "../src/api.js";
import { FakeAccountApi, mount, organization, query, shows, submit } from "./fakeAccountApi.js";

describe("<kimlik-organization-settings>", () => {
  let api: FakeAccountApi;
  let acme: MyOrganization;

  beforeEach(() => {
    api = new FakeAccountApi().configure();
    acme = organization("Acme", "acme", ["kimlik.org.settings:write"]);
    api.organizations = [acme];
  });

  it("changes the organization's settings", async () => {
    const element = await mount("kimlik-organization-settings", { organization: acme.id }, "Organization settings");
    expect(query<HTMLInputElement>(element, "input[name=name]").value).toBe("Acme");

    query<HTMLInputElement>(element, "input[name=name]").value = "Acme Corp";
    query<HTMLInputElement>(element, "input[name=requireMfa]").checked = true;
    submit(query<HTMLFormElement>(element, "section[part=settings] form"));

    await shows(element, "The settings of Acme Corp are saved.");
    expect(api.requests.find((request) => request.method === "PATCH")?.body).toEqual({ name: "Acme Corp", slug: "acme", pictureUrl: null, requireMfa: true });
    expect(query<HTMLInputElement>(element, "input[name=name]").value).toBe("Acme Corp");
  });

  it("deletes the organization only once its short name is typed", async () => {
    const element = await mount("kimlik-organization-settings", { organization: acme.id }, "Delete the organization");
    let deleted: unknown;
    document.addEventListener("kimlik-organization-delete", (event) => (deleted = (event as CustomEvent).detail), { once: true });
    const form = query<HTMLFormElement>(element, "section[part=delete] form");

    query<HTMLInputElement>(element, "input[name=confirmation]").value = "acm";
    submit(form);
    await shows(element, "Type the organization's short name to delete it.");
    expect(api.changes()).toEqual([]);

    query<HTMLInputElement>(element, "input[name=confirmation]").value = "acme";
    submit(query<HTMLFormElement>(element, "section[part=delete] form"));

    await shows(element, "Acme is deleted.");
    expect(api.changes()).toEqual([`DELETE /organizations/${acme.id}`]);
    expect(deleted).toEqual({ id: acme.id });
    expect(element.shadowRoot!.querySelector("form")).toBeNull();
  });

  it("lets every member leave, and only that", async () => {
    acme.permissions = [];
    const element = await mount("kimlik-organization-settings", { organization: acme.id }, "Leave the organization");
    expect(element.shadowRoot!.querySelectorAll("form")).toHaveLength(0);
    let left: unknown;
    document.addEventListener("kimlik-organization-leave", (event) => (left = (event as CustomEvent).detail), { once: true });

    query(element, "[data-key=leave]").click();

    await shows(element, "You left Acme.");
    expect(api.changes()).toEqual([`DELETE /organizations/${acme.id}/membership`]);
    expect(left).toEqual({ id: acme.id });
  });

  it("explains a refusal, such as being the last who can manage members", async () => {
    acme.permissions = [];
    api.refuseNext = { status: 409, code: "organization.last_administrator" };
    const element = await mount("kimlik-organization-settings", { organization: acme.id }, "Leave the organization");

    query(element, "[data-key=leave]").click();

    await shows(element, "Someone else must be able to manage the members before you leave.");
    expect(query(element, "section[part=leave]")).toBeTruthy();
  });
});
