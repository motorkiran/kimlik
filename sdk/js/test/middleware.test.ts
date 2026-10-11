import { describe, expect, it } from "vitest";
import { kimlikMiddleware, requirePermission, type Kimlik, type KimlikUser } from "../src/index.js";

function response() {
  return { statusCode: 200, headers: {} as Record<string, string>, ended: false, setHeader(name: string, value: string) { this.headers[name] = value; }, end() { this.ended = true; } };
}

const reader = { hasPermission: (permission: string) => permission === "invoices:read" } as unknown as KimlikUser;
const kimlik = { authenticate: async (header: string | null | undefined) => (header === "Bearer good" ? reader : null) } as unknown as Kimlik;

describe("middleware", () => {
  it("puts the caller on the request, or answers 401", async () => {
    const request: { headers: { authorization?: string }; kimlik?: KimlikUser } = { headers: { authorization: "Bearer good" } };
    await new Promise<void>((next) => kimlikMiddleware(kimlik)(request, response(), () => next()));
    expect(request.kimlik).toBe(reader);

    const refused = response();
    kimlikMiddleware(kimlik)({ headers: {} }, refused, () => expect.fail("next was called"));
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(refused.statusCode).toBe(401);
    expect(refused.headers["WWW-Authenticate"]).toBe("Bearer");
  });

  it("lets callers with the permission through, and answers 403 to the others", () => {
    let passed = false;
    requirePermission("invoices:read")({ headers: {}, kimlik: reader }, response(), () => (passed = true));
    expect(passed).toBe(true);

    const refused = response();
    requirePermission("invoices:write")({ headers: {}, kimlik: reader }, refused, () => expect.fail("next was called"));
    expect(refused.statusCode).toBe(403);
  });
});
