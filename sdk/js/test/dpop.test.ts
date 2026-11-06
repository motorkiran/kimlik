import { createHash } from "node:crypto";
import { calculateJwkThumbprint, exportJWK, generateKeyPair, SignJWT, type JWK } from "jose";
import { beforeEach, describe, expect, it } from "vitest";
import { createKimlik, kimlikMiddleware, type Kimlik, type KimlikUser } from "../src/index.js";
import { audience, authority, FakeKimlik } from "./fakeKimlik.js";

const url = "https://api.example.test/invoices";
const subject = "0199c3a1-0f2e-7d3c-8b4a-5e6f70819203";

/** A client's DPoP key, which makes proofs as the client would, or as one should not. */
class ClientKey {
  private constructor(private readonly privateKey: Awaited<ReturnType<typeof generateKeyPair>>["privateKey"], readonly jwk: JWK) {}

  static async create(): Promise<ClientKey> {
    const { privateKey, publicKey } = await generateKeyPair("ES256");
    return new ClientKey(privateKey, await exportJWK(publicKey));
  }

  thumbprint(): Promise<string> {
    return calculateJwkThumbprint(this.jwk, "sha256");
  }

  proof(accessToken: string, claims: Record<string, unknown> = {}, header: Record<string, unknown> = {}): Promise<string> {
    return new SignJWT({
      jti: crypto.randomUUID(),
      htm: "GET",
      htu: url,
      ath: createHash("sha256").update(accessToken).digest("base64url"),
      iat: Math.floor(Date.now() / 1000),
      ...claims,
    })
      .setProtectedHeader({ alg: "ES256", typ: "dpop+jwt", jwk: this.jwk, ...header })
      .sign(this.privateKey);
  }
}

describe("DPoP tokens", () => {
  let fake: FakeKimlik;
  let kimlik: Kimlik;
  let key: ClientKey;
  let token: string;

  beforeEach(async () => {
    fake = await FakeKimlik.create();
    kimlik = createKimlik({ authority, audience, fetch: fake.fetch });
    key = await ClientKey.create();
    token = await fake.token({ sub: subject, cnf: { jkt: await key.thumbprint() } });
  });

  const authenticate = async (proof: string | string[] | undefined, request: Partial<{ method: string; url: string }> = {}) =>
    kimlik.authenticate(`DPoP ${token}`, { method: request.method ?? "GET", url: request.url ?? `${url}?page=2`, proof });

  it("are taken with a proof by their key, once", async () => {
    const proof = await key.proof(token);

    expect((await authenticate(proof))?.userId).toBe(subject);
    expect(await authenticate(proof), "a proof is used once").toBeNull();
  });

  it("are refused as bearer tokens, and unbound tokens as DPoP tokens", async () => {
    expect(await kimlik.authenticate(`Bearer ${token}`)).toBeNull();

    const unbound = await fake.token({ sub: subject });
    expect(await kimlik.authenticate(`DPoP ${unbound}`, { method: "GET", url, proof: await key.proof(unbound) })).toBeNull();
    expect((await kimlik.authenticate(`Bearer ${unbound}`))?.userId).toBe(subject);
  });

  it("are refused with proofs that fail a check", async () => {
    const other = await ClientKey.create();
    const { privateKey } = await generateKeyPair("ES256", { extractable: true });

    expect(await authenticate(undefined), "no proof").toBeNull();
    expect(await authenticate([await key.proof(token), await key.proof(token)]), "two proofs").toBeNull();
    expect(await authenticate(await other.proof(token)), "another key").toBeNull();
    expect(await authenticate(await key.proof("another-token")), "another token").toBeNull();
    expect(await authenticate(await key.proof(token, { htm: "POST" })), "another method").toBeNull();
    expect(await authenticate(await key.proof(token, { htu: "https://api.example.test/payments" })), "another URL").toBeNull();
    expect(await authenticate(await key.proof(token, { iat: Math.floor(Date.now() / 1000) - 360 })), "too old").toBeNull();
    expect(await authenticate(await key.proof(token, { iat: Math.floor(Date.now() / 1000) + 120 })), "from the future").toBeNull();
    expect(await authenticate(await key.proof(token, { jti: undefined })), "no jti").toBeNull();
    expect(await authenticate(await key.proof(token, {}, { typ: "JWT" })), "not typed dpop+jwt").toBeNull();
    expect(await authenticate(await key.proof(token, {}, { jwk: { ...key.jwk, d: (await exportJWK(privateKey)).d } })), "a private key").toBeNull();
  });

  it("are checked by the middleware against the request's URL", async () => {
    const accepted: Request = { headers: { authorization: `DPoP ${token}`, host: "api.example.test", dpop: await key.proof(token) }, method: "GET", originalUrl: "/invoices?page=2", protocol: "https" };
    await new Promise<void>((next) => kimlikMiddleware(kimlik)(accepted, response(), () => next()));
    expect(accepted.kimlik?.userId).toBe(subject);

    // Behind a proxy that the API does not trust, the request looks like plain HTTP on another host.
    const proxied: Request = { headers: { authorization: `DPoP ${token}`, host: "internal:8080", dpop: await key.proof(token) }, method: "GET", url: "/invoices" };
    await new Promise<void>((next) => kimlikMiddleware(kimlik, { publicUrl: "https://api.example.test/" })(proxied, response(), () => next()));
    expect(proxied.kimlik?.userId).toBe(subject);

    const refused = response();
    kimlikMiddleware(kimlik)({ headers: { authorization: `DPoP ${token}`, host: "internal:8080", dpop: await key.proof(token) }, method: "GET", url: "/invoices" }, refused, () =>
      expect.fail("next was called"));
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(refused.statusCode).toBe(401);
    expect(refused.headers["WWW-Authenticate"]).toBe('Bearer, DPoP algs="ES256 ES384 PS256 RS256"');
  });
});

interface Request {
  headers: { authorization: string; host: string; dpop: string };
  method: string;
  url?: string;
  originalUrl?: string;
  protocol?: string;
  kimlik?: KimlikUser;
}

function response() {
  return { statusCode: 200, headers: {} as Record<string, string>, setHeader(name: string, value: string) { this.headers[name] = value; }, end() {} };
}
