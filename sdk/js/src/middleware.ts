import { dpopAlgorithms, type DpopRequest } from "./dpop.js";
import type { Kimlik } from "./kimlik.js";
import type { KimlikUser } from "./user.js";

/** The parts of a request and response the middleware needs, which Express, Connect and Node's own have. */
interface Request {
  headers: { authorization?: string | undefined; dpop?: string | string[] | undefined; host?: string | undefined };
  method?: string | undefined;
  url?: string | undefined;
  /** Express's path before routers took their part off. */
  originalUrl?: string | undefined;
  /** Express's scheme, which follows `X-Forwarded-Proto` when its `trust proxy` setting allows. */
  protocol?: string | undefined;
  socket?: { encrypted?: boolean | undefined } | undefined;
  kimlik?: KimlikUser;
}

interface Response {
  statusCode: number;
  setHeader(name: string, value: string): unknown;
  end(body?: string): unknown;
}

type Next = (error?: unknown) => void;

export interface KimlikMiddlewareOptions {
  /**
   * The address clients reach the API at, such as `https://api.example.com/`, which DPoP proofs name. By default, the
   * request's own scheme and `Host`, so set it behind a proxy that changes either.
   */
  publicUrl?: string | URL;
}

const challenge = `Bearer, DPoP algs="${dpopAlgorithms.join(" ")}"`;

/**
 * Puts the caller of the request on `req.kimlik`, from its access token, DPoP token with its proof, or API key, and
 * answers 401 without a valid one, as `(req, res, next)` middleware for Express, Connect and the like.
 */
export function kimlikMiddleware(kimlik: Kimlik, options: KimlikMiddlewareOptions = {}) {
  return (request: Request, response: Response, next: Next): void => {
    const authorization = request.headers.authorization;
    const dpop = /^DPoP\s/i.test(authorization ?? "") ? dpopRequest(request, options) : undefined;
    kimlik.authenticate(authorization, dpop).then(
      (user) => {
        if (!user) {
          response.statusCode = 401;
          response.setHeader("WWW-Authenticate", challenge);
          response.end();
          return;
        }

        request.kimlik = user;
        next();
      },
      next,
    );
  };
}

/** Answers 403 unless the caller that `kimlikMiddleware` put on the request holds the permission. */
export function requirePermission(permission: string) {
  return (request: Request, response: Response, next: Next): void => {
    if (request.kimlik?.hasPermission(permission)) {
      next();
      return;
    }

    response.statusCode = request.kimlik ? 403 : 401;
    response.end();
  };
}

function dpopRequest(request: Request, options: KimlikMiddlewareOptions): DpopRequest {
  const path = request.originalUrl ?? request.url ?? "/";
  const base = options.publicUrl
    ? new URL(options.publicUrl).href.replace(/\/+$/, "")
    : `${request.protocol ?? (request.socket?.encrypted ? "https" : "http")}://${request.headers.host ?? "localhost"}`;
  return { method: request.method ?? "GET", url: base + path, proof: request.headers.dpop };
}
