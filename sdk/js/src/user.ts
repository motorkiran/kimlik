import type { JWTPayload } from "jose";

/**
 * The caller behind a Kimlik access token or API key: a user, or a service client or an organization's API key acting on
 * its own behalf. It reads the same as `KimlikUser` in Kimlik.AspNetCore.
 */
export interface KimlikUser {
  /** The user ID, the client ID of a service client, or the ID of an organization's API key. */
  readonly subject: string;
  /** The client the token was issued to (RFC 9068, section 2.2). */
  readonly clientId: string | undefined;
  /** Whether the caller is a service client acting on its own behalf rather than for a user. */
  readonly isServiceClient: boolean;
  /** The API key the caller authenticated with, when it used one rather than an access token. */
  readonly apiKeyId: string | undefined;
  /** The user's ID; undefined for a service client or an organization's API key. */
  readonly userId: string | undefined;
  /** Present when the client was granted the `profile` scope. */
  readonly name: string | undefined;
  /** Present when the client was granted the `email` scope. */
  readonly email: string | undefined;
  /** The verified phone number, in E.164; present when the client was granted the `phone` scope. */
  readonly phoneNumber: string | undefined;
  /** The user's global roles, or a service client's roles. */
  readonly roles: ReadonlySet<string>;
  /** What the caller may do: the permissions of its global roles and of its roles in `organizationId`. */
  readonly permissions: ReadonlySet<string>;
  /** The organization the token acts in, if the app signed the user in to one, or whose API key it is. */
  readonly organizationId: string | undefined;
  /** The user's roles in `organizationId`. */
  readonly organizationRoles: ReadonlySet<string>;
  /** The key of the plan in effect: the organization's in an organization context, otherwise the user's. */
  readonly plan: string | undefined;
  /** Whether the subscription has add-ons or overrides, so that its entitlements are its own rather than its plan's. */
  readonly hasCustomEntitlements: boolean;
  /**
   * The administrator acting as the user, for support, when the token was issued while they signed in as them, also when
   * a client exchanged it since. Apps can show it, record it, or refuse what only the user may do.
   */
  readonly actorId: string | undefined;
  hasPermission(permission: string): boolean;
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface Caller {
  subject: string;
  clientId?: string | undefined;
  apiKeyId?: string | undefined;
  name?: string | undefined;
  email?: string | undefined;
  phoneNumber?: string | undefined;
  roles?: Iterable<string> | undefined;
  permissions?: Iterable<string> | undefined;
  organizationId?: string | undefined;
  organizationRoles?: Iterable<string> | undefined;
  plan?: string | undefined;
  hasCustomEntitlements?: boolean | undefined;
  actorId?: string | undefined;
}

export function createUser(caller: Caller): KimlikUser {
  const permissions = new Set(caller.permissions ?? []);
  const isServiceClient = caller.subject === caller.clientId;
  return Object.freeze({
    subject: caller.subject,
    clientId: caller.clientId,
    isServiceClient,
    apiKeyId: caller.apiKeyId,
    userId: !isServiceClient && caller.subject !== caller.apiKeyId && uuid.test(caller.subject) ? caller.subject : undefined,
    name: caller.name,
    email: caller.email,
    phoneNumber: caller.phoneNumber,
    roles: new Set(caller.roles ?? []),
    permissions,
    organizationId: caller.organizationId,
    organizationRoles: new Set(caller.organizationRoles ?? []),
    plan: caller.plan,
    hasCustomEntitlements: caller.hasCustomEntitlements ?? false,
    actorId: caller.actorId,
    hasPermission: (permission: string) => permissions.has(permission),
  });
}

/** The caller an access token describes. */
export function userFromToken(payload: JWTPayload): KimlikUser {
  return createUser({
    subject: text(payload.sub) ?? "",
    clientId: text(payload["client_id"]),
    name: text(payload["name"]),
    email: text(payload["email"]),
    phoneNumber: text(payload["phone_number"]),
    roles: list(payload["roles"]),
    permissions: list(payload["permissions"]),
    organizationId: text(payload["org_id"]),
    organizationRoles: list(payload["org_roles"]),
    plan: text(payload["plan"]),
    hasCustomEntitlements: payload["custom_entitlements"] === true || payload["custom_entitlements"] === "true",
    actorId: actorOf(payload["act"]),
  });
}

/**
 * The administrator in the `act` chain (RFC 8693, section 4.1): clients that exchanged the token come first, each with
 * its `client_id`, and the first actor without one is the administrator.
 */
function actorOf(act: unknown): string | undefined {
  for (let current = act; isObject(current); current = current["act"]) {
    if (!("client_id" in current)) {
      const subject = text(current["sub"]);
      return subject !== undefined && uuid.test(subject) ? subject : undefined;
    }
  }

  return undefined;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function list(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : typeof value === "string" ? [value] : [];
}
