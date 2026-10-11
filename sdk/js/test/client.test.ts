import { beforeEach, describe, expect, it } from "vitest";
import { createKimlik, type Kimlik } from "../src/index.js";
import { audience, authority, FakeKimlik } from "./fakeKimlik.js";

describe("the Management API", () => {
  let fake: FakeKimlik;
  let kimlik: Kimlik;

  beforeEach(async () => {
    fake = await FakeKimlik.create();
    kimlik = createKimlik({ authority, audience, clientId: "invoices-api", clientSecret: "a secret+with/characters", fetch: fake.fetch });
    fake.routes.set("GET /api/v1/users/0199c3a1-0f2e-7d3c-8b4a-5e6f70819203", (request) =>
      Response.json({ id: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203", email: "ada@example.com", authorization: request.headers.get("authorization") }));
  });

  it("is called with a client credentials token, which is reused until it is about to expire", async () => {
    const first = await kimlik.client.GET("/api/v1/users/{id}", { params: { path: { id: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203" } } });
    const second = await kimlik.client.GET("/api/v1/users/{id}", { params: { path: { id: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203" } } });

    expect(first.data?.email).toBe("ada@example.com");
    expect((second.data as { authorization?: string } | undefined)?.authorization).toBe("Bearer service-token-1");
    expect(fake.tokenRequests).toBe(1);
    const tokenRequest = fake.requests.find((request) => request.url.endsWith("/connect/token"))!;
    expect(tokenRequest.headers.get("authorization")).toBe(`Basic ${Buffer.from("invoices-api:a%20secret%2Bwith%2Fcharacters").toString("base64")}`);
    expect(await tokenRequest.text()).toBe("grant_type=client_credentials&scope=kimlik");
  });

  it("verifies API keys, and keeps the answer for a minute", async () => {
    let verifications = 0;
    fake.routes.set("POST /api/v1/api-keys/verify", async (request) => {
      verifications++;
      const { key } = (await request.json()) as { key: string };
      return Response.json(key === "kmk_valid"
        ? { active: true, id: "0199c3a1-3333-7d3c-8b4a-5e6f70819203", organizationId: "0199c3a1-1111-7d3c-8b4a-5e6f70819203", permissions: ["invoices:read"], plan: "pro", customEntitlements: false }
        : { active: false, customEntitlements: false });
    });

    const caller = await kimlik.authenticate("Bearer kmk_valid");
    await kimlik.authenticate("Bearer kmk_valid");

    expect(caller?.apiKeyId).toBe("0199c3a1-3333-7d3c-8b4a-5e6f70819203");
    expect(caller?.subject).toBe("0199c3a1-3333-7d3c-8b4a-5e6f70819203");
    expect(caller?.userId).toBeUndefined();
    expect(caller?.organizationId).toBe("0199c3a1-1111-7d3c-8b4a-5e6f70819203");
    expect(caller?.hasPermission("invoices:read")).toBe(true);
    expect(await kimlik.authenticate("Bearer kmk_revoked")).toBeNull();
    expect(verifications).toBe(2);
  });

  it("reads entitlements of the caller's organization, and keeps them for a minute", async () => {
    let reads = 0;
    fake.routes.set("GET /api/v1/entitlements/organization/0199c3a1-1111-7d3c-8b4a-5e6f70819203", () => {
      reads++;
      return Response.json({ plan: "pro", features: { exports: true, seats: 25, storage: null } });
    });
    const token = await fake.token({ sub: "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203", org_id: "0199c3a1-1111-7d3c-8b4a-5e6f70819203" });
    const user = (await kimlik.verifyAccessToken(token))!;

    const entitlements = await kimlik.entitlementsOf(user);
    await kimlik.entitlementsOf(user);

    expect(entitlements.features["seats"]).toBe(25);
    expect(entitlements.features["storage"]).toBeNull();
    expect(reads).toBe(1);
  });
});
