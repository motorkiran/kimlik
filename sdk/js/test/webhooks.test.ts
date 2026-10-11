import { createHmac } from "node:crypto";
import { describe, expect, it } from "vitest";
import { verifyWebhook } from "../src/index.js";

const secret = `whsec_${Buffer.from("a webhook signing secret of 32 b").toString("base64")}`;
const body = JSON.stringify({
  type: "user.created",
  timestamp: "2026-10-11T10:00:00Z",
  data: { subjectType: "user", subjectId: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203", organizationId: null, actor: { type: "kimlik", id: null } },
});

function sign(id: string, timestamp: number, payload: string, key = secret): string {
  const raw = Buffer.from(key.slice("whsec_".length), "base64");
  return `v1,${createHmac("sha256", raw).update(`${id}.${timestamp}.${payload}`).digest("base64")}`;
}

describe("verifyWebhook", () => {
  const now = new Date("2026-10-11T10:00:30Z");
  const timestamp = Math.floor(now.getTime() / 1000);

  it("returns the event of a webhook Kimlik signed", () => {
    const headers = { "webhook-id": "msg_1", "webhook-timestamp": `${timestamp}`, "webhook-signature": sign("msg_1", timestamp, body) };

    const event = verifyWebhook({ headers, body, secret, now });

    expect(event?.id).toBe("msg_1");
    expect(event?.type).toBe("user.created");
    expect(event?.data.subjectType).toBe("user");
  });

  it("takes any of several signatures, as while a secret is replaced", () => {
    const other = `whsec_${Buffer.from("another secret, being phased out").toString("base64")}`;
    const headers = new Headers({
      "webhook-id": "msg_2",
      "webhook-timestamp": `${timestamp}`,
      "webhook-signature": `${sign("msg_2", timestamp, body, other)} ${sign("msg_2", timestamp, body)}`,
    });

    expect(verifyWebhook({ headers, body, secret, now })?.id).toBe("msg_2");
  });

  it("will not check against a secret too short to be one, such as a missing setting", () => {
    const headers = { "webhook-id": "msg_4", "webhook-timestamp": `${timestamp}`, "webhook-signature": sign("msg_4", timestamp, body) };

    expect(() => verifyWebhook({ headers, body, secret: "", now })).toThrow(/at least 16 bytes/);
    expect(() => verifyWebhook({ headers: {}, body, secret: `whsec_${Buffer.from("short").toString("base64")}`, now })).toThrow(/at least 16 bytes/);
  });

  it("refuses changed bodies, wrong secrets and old timestamps", () => {
    const signature = sign("msg_3", timestamp, body);
    const headers = { "webhook-id": "msg_3", "webhook-timestamp": `${timestamp}`, "webhook-signature": signature };

    expect(verifyWebhook({ headers, body: body.replace("user.created", "user.deleted"), secret, now })).toBeNull();
    expect(verifyWebhook({ headers, body, secret: `whsec_${Buffer.from("x".repeat(32)).toString("base64")}`, now })).toBeNull();
    expect(verifyWebhook({ headers, body, secret, now: new Date(now.getTime() + 6 * 60 * 1000) })).toBeNull();
    expect(verifyWebhook({ headers: { ...headers, "webhook-signature": signature.replace("v1,", "v2,") }, body, secret, now })).toBeNull();
  });
});
