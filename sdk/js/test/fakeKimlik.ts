import { exportJWK, generateKeyPair, SignJWT, type JWTPayload } from "jose";

export const authority = "https://id.example.test/";
export const audience = "invoices-api";

/** Kimlik as the SDK sees it over HTTP: discovery, keys, the token endpoint and whatever a test adds. */
export class FakeKimlik {
  readonly requests: Request[] = [];
  readonly routes = new Map<string, (request: Request) => Response | Promise<Response>>();
  tokenRequests = 0;
  private constructor(private readonly privateKey: Awaited<ReturnType<typeof generateKeyPair>>["privateKey"], private readonly jwk: object) {}

  static async create(): Promise<FakeKimlik> {
    const { privateKey, publicKey } = await generateKeyPair("RS256");
    const fake = new FakeKimlik(privateKey, { ...(await exportJWK(publicKey)), kid: "kimlik-1", use: "sig", alg: "RS256" });
    fake.routes.set("GET /.well-known/openid-configuration", () =>
      Response.json({ issuer: authority, jwks_uri: `${authority}.well-known/jwks`, token_endpoint: `${authority}connect/token` }));
    fake.routes.set("GET /.well-known/jwks", () => Response.json({ keys: [fake.jwk] }));
    fake.routes.set("POST /connect/token", () => {
      fake.tokenRequests++;
      return Response.json({ access_token: `service-token-${fake.tokenRequests}`, token_type: "Bearer", expires_in: 3600 });
    });
    return fake;
  }

  /** An access token as Kimlik issues them, with the claims given. */
  token(claims: JWTPayload, options: { typ?: string; audience?: string; expiresIn?: string | null } = {}): Promise<string> {
    const token = new SignJWT(claims)
      .setProtectedHeader({ alg: "RS256", kid: "kimlik-1", typ: options.typ ?? "at+jwt" })
      .setIssuer(authority)
      .setAudience(options.audience ?? audience)
      .setIssuedAt();
    if (options.expiresIn !== null) {
      token.setExpirationTime(options.expiresIn ?? "5m");
    }

    return token.sign(this.privateKey);
  }

  readonly fetch = async (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const request = input instanceof Request ? input : new Request(input, init);
    this.requests.push(request.clone());
    const route = this.routes.get(`${request.method} ${new URL(request.url).pathname}`);
    return route ? route(request) : new Response(null, { status: 404 });
  };
}
