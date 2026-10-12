import { beforeEach, describe, expect, it } from "vitest";
import type { Member, MyOrganization } from "../src/api.js";
import { FakeAccountApi, me, mount, newId, organization, pendingInvitation, query, shows, submit } from "./fakeAccountApi.js";

const read = "kimlik.org.members:read";
const write = "kimlik.org.members:write";

describe("<kimlik-organization-members>", () => {
  let api: FakeAccountApi;
  let acme: MyOrganization;
  let ada: Member;
  let grace: Member;

  beforeEach(() => {
    api = new FakeAccountApi().configure();
    acme = organization("Acme", "acme", [read, write], ["manager"]);
    api.organizations = [acme];
    api.roles = [
      { key: "viewer", name: "Viewer", description: "Sees the projects.", permissions: ["projects:read"] },
      { key: "manager", name: "Manager", description: null, permissions: [read, write] },
      { key: "owner", name: "Owner", description: null, permissions: [read, write, "kimlik.org.settings:write"] },
    ];
    ada = { userId: newId(), email: "ada@example.com", name: "Ada Lovelace", roles: ["viewer"], joinedAt: "2026-10-01T10:00:00Z" };
    grace = { userId: newId(), email: "grace@example.com", name: "Grace Hopper", roles: ["owner"], joinedAt: "2026-10-01T10:00:00Z" };
    api.members = [{ userId: me, email: "me@example.com", name: "Me", roles: ["manager"], joinedAt: "2026-10-01T10:00:00Z" }, ada, grace];
    api.invitations = [pendingInvitation("linus@example.com", ["viewer"])];
  });

  it("invites people with the roles the user may give", async () => {
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Invite someone");

    const offered = [...element.shadowRoot!.querySelectorAll<HTMLInputElement>("section[part=invite] input[type=checkbox]")].map((box) => box.value);
    expect(offered).toEqual(["viewer", "manager"]);
    query<HTMLInputElement>(element, "input[name=email]").value = "barbara@example.com";
    query<HTMLInputElement>(element, "section[part=invite] input[value=viewer]").checked = true;
    submit(query<HTMLFormElement>(element, "section[part=invite] form"));

    await shows(element, "We sent an invitation to barbara@example.com.");
    expect(api.requests.find((request) => request.method === "POST")?.body).toEqual({ email: "barbara@example.com", roles: ["viewer"] });
    expect(element.shadowRoot!.textContent).toContain("barbara@example.com");
  });

  it("changes the roles of members who hold no more than the user", async () => {
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Ada Lovelace");
    expect(element.shadowRoot!.querySelector(`[data-key="edit-${grace.userId}"]`)).toBeNull();
    expect(element.shadowRoot!.querySelector(`[data-key="edit-${me}"]`)).toBeNull();

    query(element, `[data-key="edit-${ada.userId}"]`).click();
    expect((element.shadowRoot!.activeElement as HTMLElement).dataset["key"], "focus goes into the form that opened").toBe(`save-${ada.userId}`);
    query(element, `[data-key="cancel-${ada.userId}"]`).click();
    expect((element.shadowRoot!.activeElement as HTMLElement).dataset["key"], "and back to the button on cancel").toBe(`edit-${ada.userId}`);
    query(element, `[data-key="edit-${ada.userId}"]`).click();
    query<HTMLInputElement>(element, "li[part=member] form input[value=manager]").checked = true;
    submit(query<HTMLFormElement>(element, "li[part=member] form"));

    await shows(element, "The roles of Ada Lovelace are saved.");
    expect(api.requests.find((request) => request.method === "PUT")).toMatchObject({
      path: `/organizations/${acme.id}/members/${ada.userId}/roles`,
      body: { roles: ["viewer", "manager"] },
    });
  });

  it("removes members once the user confirms", async () => {
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Ada Lovelace");

    query(element, `[data-key="remove-${ada.userId}"]`).click();
    await shows(element, "Remove Ada Lovelace from the organization?");
    expect(api.changes()).toEqual([]);
    query(element, `[data-key="confirm-remove-${ada.userId}"]`).click();

    await shows(element, "Ada Lovelace is removed.");
    expect(api.changes()).toEqual([`DELETE /organizations/${acme.id}/members/${ada.userId}`]);
  });

  it("sends invitations again and revokes them", async () => {
    const [linus] = api.invitations;
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "linus@example.com");

    const resend = query(element, `[data-key="resend-${linus!.id}"]`);
    resend.focus();
    resend.click();
    await shows(element, "We sent the invitation to linus@example.com again.");
    expect((element.shadowRoot!.activeElement as HTMLElement | null)?.dataset["key"]).toBe(`resend-${linus!.id}`);
    query(element, `[data-key="revoke-${linus!.id}"]`).click();

    await shows(element, "No pending invitations.");
    expect(api.changes()).toEqual([`POST /organizations/${acme.id}/invitations/${linus!.id}/resend`, `DELETE /organizations/${acme.id}/invitations/${linus!.id}`]);
  });

  it("only shows members to those who may not manage them", async () => {
    acme.permissions = [read];
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Ada Lovelace");

    expect(element.shadowRoot!.querySelector("section[part=invite]")).toBeNull();
    expect(element.shadowRoot!.querySelectorAll("button")).toHaveLength(0);

    acme.permissions = [];
    const other = await mount("kimlik-organization-members", { organization: acme.id }, "Your roles in the organization do not allow this.");
    expect(other.shadowRoot!.textContent).not.toContain("Ada Lovelace");
  });

  it("shows Kimlik's refusals in place", async () => {
    api.refuseNext = { status: 409, code: "invitation.pending" };
    const element = await mount("kimlik-organization-members", { organization: acme.id }, "Invite someone");

    query<HTMLInputElement>(element, "input[name=email]").value = "linus@example.com";
    submit(query<HTMLFormElement>(element, "section[part=invite] form"));

    await shows(element, "That address already has an open invitation. Send it again instead.");
    expect(query(element, "[role=alert]").textContent).toBe("That address already has an open invitation. Send it again instead.");
  });

  it("names organizations the user does not belong to as not found", async () => {
    await mount("kimlik-organization-members", { organization: newId() }, "The organization does not exist, or you are not a member of it.");
  });
});
