import type { Kimlik } from "./kimlik.js";
import type { KimlikUser } from "./user.js";

/** The parts of a request and response the middleware needs, which Express, Connect and Node's own have. */
interface Request {
  headers: { authorization?: string | undefined };
  kimlik?: KimlikUser;
}

interface Response {
  statusCode: number;
  setHeader(name: string, value: string): unknown;
  end(body?: string): unknown;
}

type Next = (error?: unknown) => void;

/**
 * Puts the caller of the request on `req.kimlik`, from its access token or API key, and answers 401 without a valid one,
 * as `(req, res, next)` middleware for Express, Connect and the like.
 */
export function kimlikMiddleware(kimlik: Kimlik) {
  return (request: Request, response: Response, next: Next): void => {
    kimlik.authenticate(request.headers.authorization).then(
      (user) => {
        if (!user) {
          response.statusCode = 401;
          response.setHeader("WWW-Authenticate", "Bearer");
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
