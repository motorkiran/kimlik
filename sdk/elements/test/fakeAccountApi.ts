import { vi } from "vitest";
import type { Invitation, Member, MyInvitation, MyOrganization, Role } from "../src/api.js";
import { configureKimlik } from "../src/index.js";

export const authority = "https://id.example.test/";

/** An access token whose subject is the signed-in user, as the components read it; the signature is not checked here. */
export const me = "0199c3a1-0000-7d3c-8b4a-5e6f70819203";
const token = `e30.${btoa(JSON.stringify({ sub: me })).replace(/=+$/, "")}.signature`;

export interface SentRequest {
  method: string;
  path: string;
  body: unknown;
  authorization: string | null;
}

/**
 * The Account API as the components see it, kept in memory: what a test puts in its lists is what the API returns, and
 * changes the components make go into them.
 */
export class FakeAccountApi {
  readonly requests: SentRequest[] = [];
  organizations: MyOrganization[] = [];
  invitationsToMe: MyInvitation[] = [];
  members: Member[] = [];
  roles: Role[] = [];
  invitations: Invitation[] = [];
  /** A problem to answer the next change with, instead of making it. */
  refuseNext: { status: number; code: string } | undefined;

  configure(): this {
    configureKimlik({ authority, accessToken: () => token, fetch: this.fetch });
    return this;
  }

  readonly fetch = async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const request = input instanceof Request ? input : new Request(input, init);
    const url = new URL(request.url);
    const path = url.pathname.replace(/^\/api\/v1\/me/, "");
    const body = request.method === "GET" || request.method === "DELETE" ? undefined : await request.json().catch(() => undefined);
    this.requests.push({ method: request.method, path, body, authorization: request.headers.get("authorization") });

    if (request.method !== "GET" && this.refuseNext) {
      const { status, code } = this.refuseNext;
      this.refuseNext = undefined;
      return Response.json({ status, code, title: "Refused", detail: `Refused with ${code}.` }, { status });
    }

    return this.answer(request.method, path, body as Record<string, unknown> | undefined);
  };

  /** The changes the components sent, as `METHOD /path`. */
  changes(): string[] {
    return this.requests.filter((request) => request.method !== "GET").map((request) => `${request.method} ${request.path}`);
  }

  private answer(method: string, path: string, body: Record<string, unknown> | undefined): Response {
    const route = `${method} ${path.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, "{id}")}`;
    const ids = path.match(/[0-9a-f]{8}-[0-9a-f-]{27}/g) ?? [];
    switch (route) {
      case "GET /organizations":
        return Response.json(this.organizations);
      case "POST /organizations":
        return Response.json({ ...organization("Created", "created"), ...body }, { status: 201 });
      case "PATCH /organizations/{id}": {
        const updated = { ...this.organizations.find((candidate) => candidate.id === ids[0])!, ...body } as MyOrganization;
        this.organizations = this.organizations.map((candidate) => (candidate.id === updated.id ? updated : candidate));
        return Response.json(updated);
      }
      case "DELETE /organizations/{id}":
      case "DELETE /organizations/{id}/membership":
        this.organizations = this.organizations.filter((candidate) => candidate.id !== ids[0]);
        return new Response(null, { status: 204 });
      case "GET /organizations/{id}/members":
        return Response.json({ items: this.members, nextCursor: null });
      case "GET /organizations/{id}/roles":
        return Response.json(this.roles);
      case "PUT /organizations/{id}/members/{id}/roles": {
        const member = this.members.find((candidate) => candidate.userId === ids[1])!;
        member.roles = body!["roles"] as string[];
        return Response.json(member);
      }
      case "DELETE /organizations/{id}/members/{id}":
        this.members = this.members.filter((candidate) => candidate.userId !== ids[1]);
        return new Response(null, { status: 204 });
      case "GET /organizations/{id}/invitations":
        return Response.json({ items: this.invitations, nextCursor: null });
      case "POST /organizations/{id}/invitations": {
        const invitation = pendingInvitation(body!["email"] as string, body!["roles"] as string[]);
        this.invitations.push(invitation);
        return Response.json(invitation, { status: 201 });
      }
      case "POST /organizations/{id}/invitations/{id}/resend":
        return Response.json(this.invitations.find((candidate) => candidate.id === ids[1]));
      case "DELETE /organizations/{id}/invitations/{id}":
        this.invitations = this.invitations.filter((candidate) => candidate.id !== ids[1]);
        return new Response(null, { status: 204 });
      case "GET /invitations":
        return Response.json(this.invitationsToMe);
      case "POST /invitations/{id}/accept":
      case "POST /invitations/{id}/decline": {
        const invitation = this.invitationsToMe.find((candidate) => candidate.id === ids[0])!;
        this.invitationsToMe = this.invitationsToMe.filter((candidate) => candidate !== invitation);
        if (route.endsWith("accept")) {
          this.organizations.push({ ...organization(invitation.organizationName, "joined"), id: invitation.organizationId, roles: invitation.roles });
        }

        return new Response(null, { status: 204 });
      }
      default:
        return new Response(null, { status: 404 });
    }
  }
}

let next = 0;

/** A new ID in the form Kimlik's are, so that the fake can tell IDs in paths. */
export function newId(): string {
  next++;
  return `0199c3a1-${next.toString(16).padStart(4, "0")}-7d3c-8b4a-5e6f70819203`;
}

export function organization(name: string, slug: string, permissions: string[] = [], roles: string[] = []): MyOrganization {
  return { id: newId(), name, slug, pictureUrl: null, roles, permissions, joinedAt: "2026-10-01T10:00:00Z", requireMfa: false, publicMetadata: {} };
}

export function pendingInvitation(email: string, roles: string[] = []): Invitation {
  return { id: newId(), email, roles, status: "pending", expiresAt: "2026-10-20T10:00:00Z", createdAt: "2026-10-13T10:00:00Z" };
}

/** Puts the element on the page and waits until it shows `text`. */
export async function mount<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  attributes: Record<string, string>,
  text: string,
): Promise<HTMLElementTagNameMap[K]> {
  const element = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    element.setAttribute(name, value);
  }

  document.body.replaceChildren(element);
  await shows(element, text);
  return element;
}

/** Waits until the element's shadow root shows `text`. */
export async function shows(element: HTMLElement, text: string): Promise<void> {
  await vi.waitFor(() => {
    if (!element.shadowRoot!.textContent!.includes(text)) {
      throw new Error(`"${text}" is not shown in: ${element.shadowRoot!.textContent}`);
    }
  });
}

export function query<T extends Element = HTMLElement>(element: HTMLElement, selector: string): T {
  const found = element.shadowRoot!.querySelector<T>(selector);
  if (!found) {
    throw new Error(`Nothing matches ${selector}.`);
  }

  return found;
}

export function submit(form: HTMLFormElement): void {
  form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
}
