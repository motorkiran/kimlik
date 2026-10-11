import { createHmac, timingSafeEqual } from "node:crypto";

/** An event Kimlik delivered, as its webhooks carry it. */
export interface WebhookEvent {
  /** The `webhook-id` of the delivery: the same for every attempt, so handle each one once. */
  readonly id: string;
  /** Such as `user.created`. */
  readonly type: string;
  readonly timestamp: string;
  readonly data: {
    /** What the event is about, such as a `user`, and its ID. */
    readonly subjectType: string;
    readonly subjectId: string;
    /** The organization it happened in, if any. */
    readonly organizationId: string | null;
    /** Who caused it: a `user`, a `client`, `kimlik` itself or someone `anonymous`. */
    readonly actor: { readonly type: string; readonly id: string | null };
    /** Details that depend on the event type, such as the roles of a new membership. */
    readonly details?: unknown;
  };
}

/** How far a webhook's timestamp may be from the current time, which turns replayed requests away. */
export const webhookTolerance = 5 * 60 * 1000;

const secretPrefix = "whsec_";
const minimumKeyLength = 16;
const signatureVersion = "v1,";

type Headers = Record<string, string | string[] | undefined> | { get(name: string): string | null };

/**
 * Checks that a webhook comes from Kimlik, as the Standard Webhooks specification describes: its signature must match
 * the endpoint's `whsec_…` secret, and its timestamp must be recent. Returns the event, or `null` when it is not one.
 * Pass the body exactly as it arrived. Delivery is at least once, so handle each event ID once.
 */
export function verifyWebhook(options: { headers: Headers; body: string; secret: string; now?: Date }): WebhookEvent | null {
  const key = Buffer.from(options.secret.startsWith(secretPrefix) ? options.secret.slice(secretPrefix.length) : options.secret, "base64");
  if (key.length < minimumKeyLength) {
    // Such as an empty one from a missing setting, which would let anyone sign.
    throw new Error("The webhook secret is not an endpoint's whsec_… secret: it must hold at least 16 bytes.");
  }

  const id = header(options.headers, "webhook-id");
  const timestamp = header(options.headers, "webhook-timestamp");
  const signatures = header(options.headers, "webhook-signature");
  if (!id || !signatures || !timestamp || !/^\d+$/.test(timestamp)) {
    return null;
  }

  const now = (options.now ?? new Date()).getTime();
  if (Math.abs(now - Number(timestamp) * 1000) > webhookTolerance) {
    return null;
  }

  const expected = createHmac("sha256", key).update(`${id}.${timestamp}.${options.body}`, "utf8").digest();

  // The header can carry several signatures, such as while a secret is being replaced.
  const signed = signatures.split(" ").some((signature) => {
    if (!signature.startsWith(signatureVersion)) {
      return false;
    }

    const candidate = Buffer.from(signature.slice(signatureVersion.length), "base64");
    return candidate.length === expected.length && timingSafeEqual(candidate, expected);
  });
  if (!signed) {
    return null;
  }

  try {
    return { ...(JSON.parse(options.body) as Omit<WebhookEvent, "id">), id };
  } catch {
    return null;
  }
}

function header(headers: Headers, name: string): string | undefined {
  if (typeof (headers as { get?: unknown }).get === "function") {
    return (headers as { get(name: string): string | null }).get(name) ?? undefined;
  }

  const value = (headers as Record<string, string | string[] | undefined>)[name];
  return Array.isArray(value) ? value[0] : value;
}
