import { currentOptions } from "./config.js";
import type { components } from "./schema.js";

type Schemas = components["schemas"];
export type MyOrganization = Schemas["MyOrganizationResponse"];
export type MyInvitation = Schemas["MyInvitationResponse"];
export type Member = Schemas["MemberResponse"];
export type Invitation = Schemas["InvitationResponse"];
export type Role = Schemas["MyOrganizationRoleResponse"];

/** A refusal from Kimlik, with the problem's `code`, such as `organization.slug_taken`, when it has one. */
export class KimlikApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
    this.name = "KimlikApiError";
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const options = currentOptions();
  if (!options) {
    throw new Error("Call configureKimlik before using Kimlik's components.");
  }

  const base = new URL(options.authority).href.replace(/\/+$/, "");
  const headers: Record<string, string> = { Authorization: `Bearer ${await options.accessToken()}` };
  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const response = await (options.fetch ?? globalThis.fetch)(`${base}/api/v1/me${path}`, {
    method,
    headers,
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as { code?: string; detail?: string; title?: string } | undefined;
    throw new KimlikApiError(response.status, problem?.code, problem?.detail ?? problem?.title ?? `Kimlik answered ${response.status}.`);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

/** Every item of a paged list, up to a thousand. */
async function all<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  let cursor: string | null | undefined;
  do {
    const query = new URLSearchParams({ limit: "200" });
    if (cursor) {
      query.set("cursor", cursor);
    }

    const page = await call<{ items: T[]; nextCursor?: string | null }>("GET", `${path}?${query}`);
    items.push(...page.items);
    cursor = page.nextCursor;
  } while (cursor && items.length < 1000);

  return items;
}

const organization = (id: string) => `/organizations/${encodeURIComponent(id)}`;

/** The Account API calls the components make, for the signed-in user. */
export const accountApi = {
  organizations: () => call<MyOrganization[]>("GET", "/organizations"),
  createOrganization: (name: string, slug: string) => call<MyOrganization>("POST", "/organizations", { name, slug }),
  updateOrganization: (id: string, changes: Schemas["UpdateMyOrganizationRequest"]) => call<MyOrganization>("PATCH", organization(id), changes),
  deleteOrganization: (id: string) => call<void>("DELETE", organization(id)),
  leave: (id: string) => call<void>("DELETE", `${organization(id)}/membership`),

  members: (id: string) => all<Member>(`${organization(id)}/members`),
  roles: (id: string) => call<Role[]>("GET", `${organization(id)}/roles`),
  setRoles: (id: string, userId: string, roles: string[]) =>
    call<Member>("PUT", `${organization(id)}/members/${encodeURIComponent(userId)}/roles`, { roles }),
  removeMember: (id: string, userId: string) => call<void>("DELETE", `${organization(id)}/members/${encodeURIComponent(userId)}`),

  invitations: async (id: string) => (await all<Invitation>(`${organization(id)}/invitations`)).filter((invitation) => invitation.status === "pending"),
  invite: (id: string, email: string, roles: string[]) => call<Invitation>("POST", `${organization(id)}/invitations`, { email, roles }),
  resend: (id: string, invitationId: string) => call<Invitation>("POST", `${organization(id)}/invitations/${encodeURIComponent(invitationId)}/resend`),
  revoke: (id: string, invitationId: string) => call<void>("DELETE", `${organization(id)}/invitations/${encodeURIComponent(invitationId)}`),

  invitationsToMe: () => call<MyInvitation[]>("GET", "/invitations"),
  accept: (invitationId: string) => call<void>("POST", `/invitations/${encodeURIComponent(invitationId)}/accept`),
  decline: (invitationId: string) => call<void>("POST", `/invitations/${encodeURIComponent(invitationId)}/decline`),
};

/** Whether the permission is one of Kimlik's own within organizations, which members can only give if they hold it. */
export const isOrganizationSystemPermission = (permission: string) => permission.startsWith("kimlik.org.");
