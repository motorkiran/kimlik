import { beforeEach, describe, expect, it } from "vitest";
import { createKimlik, type Kimlik } from "../src/index.js";
import { audience, authority, FakeKimlik } from "./fakeKimlik.js";

describe("access tokens", () => {
  let fake: FakeKimlik;
  let kimlik: Kimlik;

  beforeEach(async () => {
    fake = await FakeKimlik.create();
    kimlik = createKimlik({ authority, audience, fetch: fake.fetch });
  });

  it("describe the user and what they may do", async () => {
    const token = await fake.token({
      sub: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203",
      client_id: "invoices-web",
      email: "ada@example.com",
      roles: ["staff"],
      permissions: ["invoices:read", "invoices:write"],
      org_id: "0199c3a1-1111-7d3c-8b4a-5e6f70819203",
      org_roles: ["accountant"],
      plan: "pro",
      custom_entitlements: true,
    });

    const user = await kimlik.authenticate(`Bearer ${token}`);

    expect(user?.userId).toBe("0199c3a1-0f2e-7d3c-8b4a-5e6f70819203");
    expect(user?.isServiceClient).toBe(false);
    expect(user?.email).toBe("ada@example.com");
    expect(user?.hasPermission("invoices:write")).toBe(true);
    expect(user?.hasPermission("invoices:delete")).toBe(false);
    expect(user?.organizationId).toBe("0199c3a1-1111-7d3c-8b4a-5e6f70819203");
    expect([...(user?.organizationRoles ?? [])]).toEqual(["accountant"]);
    expect(user?.plan).toBe("pro");
    expect(user?.hasCustomEntitlements).toBe(true);
  });

  it("name the administrator acting as the user, behind the clients that exchanged the token", async () => {
    const token = await fake.token({
      sub: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203",
      act: { sub: "reports-api", client_id: "reports-api", act: { sub: "0199c3a1-2222-7d3c-8b4a-5e6f70819203" } },
    });

    expect((await kimlik.verifyAccessToken(token))?.actorId).toBe("0199c3a1-2222-7d3c-8b4a-5e6f70819203");
  });

  it("tell service clients from users", async () => {
    const token = await fake.token({ sub: "report-worker", client_id: "report-worker", permissions: ["reports:read"] });

    const caller = await kimlik.verifyAccessToken(token);

    expect(caller?.isServiceClient).toBe(true);
    expect(caller?.userId).toBeUndefined();
  });

  it("are refused for other audiences, other types, once expired and without an expiry", async () => {
    const claims = { sub: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203" };

    expect(await kimlik.verifyAccessToken(await fake.token(claims, { audience: "other-api" }))).toBeNull();
    expect(await kimlik.verifyAccessToken(await fake.token(claims, { typ: "JWT" }))).toBeNull();
    expect(await kimlik.verifyAccessToken(await fake.token(claims, { expiresIn: "-5m" }))).toBeNull();
    expect(await kimlik.verifyAccessToken(await fake.token(claims, { expiresIn: null }))).toBeNull();
    expect(await kimlik.authenticate("Basic abc")).toBeNull();
    expect(await kimlik.authenticate(undefined)).toBeNull();
  });
});
