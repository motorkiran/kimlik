import { createHash } from "node:crypto";
import { calculateJwkThumbprint, decodeProtectedHeader, errors, importJWK, jwtVerify, type JWK } from "jose";

/** What a request with a DPoP token (RFC 9449) brings to prove that its sender holds the token's key. */
export interface DpopRequest {
  /** The request's method, such as `GET`. */
  method: string;
  /** The request's full URL, as the client called it; the query is ignored. Behind a proxy, the public one. */
  url: string | URL;
  /** The request's `DPoP` header. */
  proof: string | readonly string[] | null | undefined;
}

/** The algorithms Kimlik takes for proofs, which the SDK takes too. */
export const dpopAlgorithms = ["ES256", "ES384", "PS256", "RS256"] as const;

const maxAge = 300;
const maxClockSkew = 60;
const curves: Record<string, string> = { ES256: "P-256", ES384: "P-384" };

/**
 * Proofs accepted in the last five minutes, by key and `jti`, so that none is accepted twice by this process. Several
 * processes do not share them: there, the short window and TLS do the rest.
 */
export class DpopReplayCache {
  readonly #used = new Map<string, number>();

  /** Records the proof; `false` if it was recorded before. */
  use(key: string, expiresAt: number): boolean {
    const now = Date.now();
    if ((this.#used.get(key) ?? 0) > now) {
      return false;
    }

    // A map keeps the order entries came in, and every entry lives as long, so the expired ones come first.
    for (const [usedKey, usedUntil] of this.#used) {
      if (usedUntil > now) {
        break;
      }

      this.#used.delete(usedKey);
    }

    this.#used.delete(key);
    this.#used.set(key, expiresAt);
    return true;
  }
}

/**
 * The JWK SHA-256 thumbprint of the key that signed the request's proof, made for `accessToken`, or `null` unless the
 * proof passes every check of RFC 9449, section 4.3, and was not used before.
 */
export async function verifyDpopProof(request: DpopRequest, accessToken: string, replay: DpopReplayCache): Promise<string | null> {
  const proofs = typeof request.proof === "string" ? [request.proof] : (request.proof ?? []);
  const [proof] = proofs;
  if (proofs.length !== 1 || !proof) {
    return null;
  }

  try {
    const header = decodeProtectedHeader(proof);
    const jwk = header.jwk as JWK | undefined;
    if (header.typ?.toLowerCase() !== "dpop+jwt" || !isAlgorithm(header.alg) || !jwk || !isPublicKeyFor(jwk, header.alg)) {
      return null;
    }

    const { payload } = await jwtVerify(proof, await importJWK(jwk, header.alg), { typ: "dpop+jwt", algorithms: [...dpopAlgorithms] });
    const now = Math.floor(Date.now() / 1000);
    const target = new URL(request.url);
    if (typeof payload.jti !== "string" || payload.jti.length === 0 || payload.jti.length > 256
      || typeof payload.iat !== "number" || payload.iat > now + maxClockSkew || payload.iat < now - maxAge
      || payload.htm !== request.method || typeof payload.htu !== "string" || !URL.canParse(payload.htu)
      || !sameTarget(new URL(payload.htu), target)
      || payload.ath !== createHash("sha256").update(accessToken, "ascii").digest("base64url")) {
      return null;
    }

    const thumbprint = await calculateJwkThumbprint(jwk, "sha256");
    const replayKey = createHash("sha256").update(`${thumbprint}.${payload.jti}`).digest("hex");
    return replay.use(replayKey, (payload.iat + maxAge) * 1000) ? thumbprint : null;
  } catch (error) {
    if (error instanceof errors.JOSEError || error instanceof TypeError) {
      return null;
    }

    throw error;
  }
}

function isAlgorithm(alg: string | undefined): alg is (typeof dpopAlgorithms)[number] {
  return (dpopAlgorithms as readonly (string | undefined)[]).includes(alg);
}

function isPublicKeyFor(jwk: JWK, alg: string): boolean {
  if (jwk.d !== undefined || jwk.p !== undefined || jwk.q !== undefined || jwk.k !== undefined) {
    return false;
  }

  if (jwk.kty === "EC") {
    return curves[alg] === jwk.crv;
  }

  return jwk.kty === "RSA" && (alg === "RS256" || alg === "PS256") && typeof jwk.n === "string" && modulusLength(jwk.n) >= 256;
}

/** The modulus's length in bytes, without the leading zeros some encoders add. */
function modulusLength(modulus: string): number {
  const bytes = Buffer.from(modulus, "base64url");
  const first = bytes.findIndex((byte) => byte !== 0);
  return first < 0 ? 0 : bytes.length - first;
}

/** The same scheme, host, port and path, as RFC 9449 compares `htu` with the request's URL. */
function sameTarget(claimed: URL, actual: URL): boolean {
  return claimed.origin === actual.origin && claimed.pathname === actual.pathname;
}
