import { createRemoteJWKSet, customFetch, errors, jwtVerify, type JWTVerifyGetKey } from "jose";
import createClient, { type Client } from "openapi-fetch";
import type { paths } from "./schema.js";
import { createUser, userFromToken, type KimlikUser } from "./user.js";

export type ManagementClient = Client<paths>;

/** The features and limits of a subscriber: `true` or `false` for a feature, a number or `null` (unlimited) for a limit. */
export interface Entitlements {
  /** The key of the plan in effect, if any. */
  readonly plan: string | null;
  /** Each feature's value, by key. */
  readonly features: Readonly<Record<string, boolean | number | null>>;
}

export interface KimlikOptions {
  /** Kimlik's address, its token issuer, such as `https://id.example.com/`. */
  authority: string | URL;
  /** Your API's audience: the scope of its API resource. Access tokens for other audiences are refused. */
  audience?: string;
  /**
   * Your API's service client, which API keys, entitlements and the Management API need; give its role the permissions
   * those take, such as `kimlik.api_keys:verify` and `kimlik.subscriptions:read`.
   */
  clientId?: string;
  clientSecret?: string;
  /** How far token lifetimes may be off, in seconds; 60 by default. */
  clockTolerance?: number;
  /** The fetch to call Kimlik with; the global one by default. */
  fetch?: typeof fetch;
}

export interface Kimlik {
  /** The caller of a valid access token for the audience, or `null`. */
  verifyAccessToken(token: string): Promise<KimlikUser | null>;
  /**
   * The caller of an `Authorization` header, from a bearer access token or, with a service client, an API key
   * (`Bearer kmk_…`); `null` without a valid one. API keys are verified through the Management API and cached a minute.
   */
  authenticate(authorization: string | null | undefined): Promise<KimlikUser | null>;
  /**
   * The features and limits of the caller's subscriber: the organization in an organization context, otherwise the user.
   * Read through the Management API and cached a minute.
   */
  entitlementsOf(user: KimlikUser): Promise<Entitlements>;
  /** The Management API, typed from its OpenAPI document; it gets and renews its access tokens by itself. */
  readonly client: ManagementClient;
}

interface Discovery {
  issuer: string;
  jwks_uri: string;
  token_endpoint: string;
}

const apiKeyPrefix = "kmk_";
const cacheLifetime = 60_000;
const cacheLimit = 10_000;

export function createKimlik(options: KimlikOptions): Kimlik {
  const fetchImpl = options.fetch ?? globalThis.fetch;
  const authority = new URL(options.authority);
  const base = authority.href.replace(/\/+$/, "");
  let discovery: Promise<Discovery> | undefined;
  let keys: JWTVerifyGetKey | undefined;

  const discover = () =>
    (discovery ??= (async () => {
      const response = await fetchImpl(`${base}/.well-known/openid-configuration`);
      if (!response.ok) {
        discovery = undefined;
        throw new Error(`Kimlik's discovery document could not be read: ${response.status}.`);
      }

      return (await response.json()) as Discovery;
    })());

  const tokens = new ClientCredentialsTokens(options, fetchImpl, async () => (await discover()).token_endpoint);
  const client = createClient<paths>({ baseUrl: base, fetch: (request) => fetchImpl(request) });
  client.use({
    async onRequest({ request }) {
      request.headers.set("Authorization", `Bearer ${await tokens.get()}`);
      return request;
    },
  });

  const apiKeys = new Cache<KimlikUser | null>();
  const entitlements = new Cache<Entitlements>();

  async function verifyAccessToken(token: string): Promise<KimlikUser | null> {
    if (!options.audience) {
      throw new Error("Set the audience to verify access tokens.");
    }

    const { issuer, jwks_uri } = await discover();
    keys ??= createRemoteJWKSet(new URL(jwks_uri), { [customFetch]: fetchImpl });
    try {
      const { payload } = await jwtVerify(token, keys, {
        issuer,
        audience: options.audience,
        typ: "at+jwt",
        clockTolerance: options.clockTolerance ?? 60,
      });
      return typeof payload.sub === "string" ? userFromToken(payload) : null;
    } catch (error) {
      if (error instanceof errors.JOSEError) {
        return null;
      }

      throw error;
    }
  }

  async function verifyApiKey(key: string): Promise<KimlikUser | null> {
    return apiKeys.get(key, async () => {
      const { data, error } = await client.POST("/api/v1/api-keys/verify", { body: { key } });
      if (error || !data?.active || !data.id) {
        return null;
      }

      // An organization's key acts on its own behalf, with the key as the subject; a user's key acts for the user.
      return createUser({
        subject: data.userId ?? data.id,
        apiKeyId: data.id,
        permissions: data.permissions ?? [],
        organizationId: data.organizationId ?? undefined,
        plan: data.plan ?? undefined,
        hasCustomEntitlements: data.customEntitlements ?? false,
      });
    });
  }

  return {
    verifyAccessToken,
    async authenticate(authorization) {
      const match = /^Bearer\s+(\S+)\s*$/i.exec(authorization ?? "");
      if (!match?.[1]) {
        return null;
      }

      const credential = match[1];
      return credential.startsWith(apiKeyPrefix) ? (options.clientId ? verifyApiKey(credential) : null) : verifyAccessToken(credential);
    },
    async entitlementsOf(user) {
      const [subscriberType, id] = user.organizationId ? (["organization", user.organizationId] as const) : (["user", user.userId] as const);
      if (!id) {
        return { plan: user.plan ?? null, features: {} };
      }

      return entitlements.get(`${subscriberType}:${id}`, async () => {
        const { data, error } = await client.GET("/api/v1/entitlements/{subscriberType}/{id}", { params: { path: { subscriberType, id } } });
        if (error || !data) {
          throw new Error(`The entitlements of ${subscriberType} ${id} could not be read.`);
        }

        return data as unknown as Entitlements;
      });
    },
    client,
  };
}

/** Client credentials tokens for the Management API, renewed a minute before they expire. */
class ClientCredentialsTokens {
  #token: { value: string; renewAt: number } | undefined;
  #pending: Promise<string> | undefined;

  constructor(
    private readonly options: KimlikOptions,
    private readonly fetchImpl: typeof fetch,
    private readonly endpoint: () => Promise<string>,
  ) {}

  get(): Promise<string> {
    if (this.#token && Date.now() < this.#token.renewAt) {
      return Promise.resolve(this.#token.value);
    }

    return (this.#pending ??= this.request().finally(() => (this.#pending = undefined)));
  }

  private async request(): Promise<string> {
    const { clientId, clientSecret } = this.options;
    if (!clientId || !clientSecret) {
      throw new Error("Set clientId and clientSecret to call the Management API.");
    }

    const credentials = Buffer.from(`${encodeURIComponent(clientId)}:${encodeURIComponent(clientSecret)}`).toString("base64");
    const response = await this.fetchImpl(await this.endpoint(), {
      method: "POST",
      headers: { Authorization: `Basic ${credentials}`, "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ grant_type: "client_credentials", scope: "kimlik" }),
    });
    if (!response.ok) {
      throw new Error(`Kimlik refused the service client's credentials: ${response.status}.`);
    }

    const token = (await response.json()) as { access_token: string; expires_in: number };
    this.#token = { value: token.access_token, renewAt: Date.now() + Math.max(0, token.expires_in - 60) * 1000 };
    return token.access_token;
  }
}

/** Values kept for a minute, and at most ten thousand of them, so that a burst of callers does not grow it for good. */
class Cache<T> {
  readonly #entries = new Map<string, { value: Promise<T>; expiresAt: number }>();

  get(key: string, load: () => Promise<T>): Promise<T> {
    const now = Date.now();
    const cached = this.#entries.get(key);
    if (cached && now < cached.expiresAt) {
      return cached.value;
    }

    if (this.#entries.size >= cacheLimit) {
      for (const [stale, entry] of this.#entries) {
        if (entry.expiresAt <= now) {
          this.#entries.delete(stale);
        }
      }

      if (this.#entries.size >= cacheLimit) {
        this.#entries.clear();
      }
    }

    const value = load();
    this.#entries.set(key, { value, expiresAt: now + cacheLifetime });
    value.catch(() => this.#entries.delete(key));
    return value;
  }
}
