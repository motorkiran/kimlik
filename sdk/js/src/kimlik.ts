import { createHash } from "node:crypto";
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
  /**
   * Whether Kimlik's addresses must use HTTPS, so that keys and tokens cannot be tampered with on the way; `true` by
   * default. Addresses on the local machine may use HTTP either way.
   */
  requireHttps?: boolean;
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
const apiKeyPattern = /^kmk_[A-Za-z0-9_-]{43}$/;
const cacheLifetime = 60_000;
const cacheLimit = 10_000;

export function createKimlik(options: KimlikOptions): Kimlik {
  const fetchImpl = options.fetch ?? globalThis.fetch;
  const secure = (url: URL) =>
    url.protocol === "https:" || (url.protocol === "http:" && (options.requireHttps === false || isLoopback(url.hostname)));
  const authority = new URL(options.authority);
  if (!secure(authority)) {
    throw new Error("Kimlik's authority must use HTTPS, unless it is on this machine or requireHttps is false.");
  }

  const base = authority.href.replace(/\/+$/, "");
  let discovery: Promise<Discovery> | undefined;
  let keys: JWTVerifyGetKey | undefined;

  const discover = () =>
    (discovery ??= (async () => {
      const response = await fetchImpl(`${base}/.well-known/openid-configuration`);
      if (!response.ok) {
        throw new Error(`Kimlik's discovery document could not be read: ${response.status}.`);
      }

      // The issuer must be the authority it was read from, and keys and tokens must come from addresses as safe.
      const document = (await response.json()) as Partial<Discovery>;
      if (typeof document.issuer !== "string" || document.issuer.replace(/\/+$/, "") !== base
        || !isSecureUrl(document.jwks_uri, secure) || !isSecureUrl(document.token_endpoint, secure)) {
        throw new Error(`Kimlik's discovery document does not describe ${authority.href}.`);
      }

      return document as Discovery;
    })().catch((error: unknown) => {
      // Read again next time, as after an outage.
      discovery = undefined;
      throw error;
    }));

  const tokens = new ClientCredentialsTokens(options, fetchImpl, async () => (await discover()).token_endpoint);
  const client = createClient<paths>({
    baseUrl: base,
    async fetch(request) {
      // A token Kimlik no longer takes, such as after the client's roles or keys changed, is replaced once.
      const retry = request.clone();
      const token = await tokens.get();
      request.headers.set("Authorization", `Bearer ${token}`);
      const response = await fetchImpl(request);
      if (response.status !== 401) {
        return response;
      }

      await response.body?.cancel();
      tokens.forget(token);
      retry.headers.set("Authorization", `Bearer ${await tokens.get()}`);
      return fetchImpl(retry);
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
        requiredClaims: ["exp"],
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
    // Made-up values are turned away here, without a call to Kimlik or an entry in the cache, which holds hashes of keys.
    if (!apiKeyPattern.test(key)) {
      return null;
    }

    return apiKeys.get(createHash("sha256").update(key).digest("hex"), async () => {
      const { data, response } = await client.POST("/api/v1/api-keys/verify", { body: { key } });
      if (!response.ok) {
        // Not an answer about the key, so it is not kept.
        throw new Error(`API keys could not be verified: ${response.status}.`);
      }

      if (!data?.active || !data.id) {
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

  /** Stops using the token, unless another request has replaced it already. */
  forget(token: string): void {
    if (this.#token?.value === token) {
      this.#token = undefined;
    }
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

/**
 * Values kept for a minute, and at most ten thousand of them, so that a burst of callers does not grow it for good: the
 * oldest go first. Failures are not kept.
 */
class Cache<T> {
  readonly #entries = new Map<string, { value: Promise<T>; expiresAt: number }>();

  get(key: string, load: () => Promise<T>): Promise<T> {
    const now = Date.now();
    const cached = this.#entries.get(key);
    if (cached && now < cached.expiresAt) {
      return cached.value;
    }

    // A map keeps the order entries came in, so the first ones are the oldest.
    this.#entries.delete(key);
    for (const oldest of this.#entries.keys()) {
      if (this.#entries.size < cacheLimit) {
        break;
      }

      this.#entries.delete(oldest);
    }

    const value = load();
    const entry = { value, expiresAt: now + cacheLifetime };
    this.#entries.set(key, entry);
    value.catch(() => {
      if (this.#entries.get(key) === entry) {
        this.#entries.delete(key);
      }
    });
    return value;
  }
}

function isLoopback(hostname: string): boolean {
  return hostname === "localhost" || hostname === "[::1]" || /^127(\.\d{1,3}){3}$/.test(hostname);
}

function isSecureUrl(value: unknown, secure: (url: URL) => boolean): boolean {
  return typeof value === "string" && URL.canParse(value) && secure(new URL(value));
}
