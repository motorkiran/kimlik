export interface KimlikElementsOptions {
  /** Kimlik's address, such as `https://id.example.com/`. */
  authority: string | URL;
  /**
   * Returns a current access token for the `kimlik.organizations` scope, such as one from your OpenID Connect library.
   * It is called before every request, so it can renew the token when it is about to expire.
   */
  accessToken: () => string | Promise<string>;
  /** The fetch to call Kimlik with; the global one by default. */
  fetch?: typeof fetch;
}

let current: KimlikElementsOptions | undefined;
const listeners = new Set<() => void>();

/** Tells the components where Kimlik is and how to get an access token; they load once it is called. */
export function configureKimlik(options: KimlikElementsOptions): void {
  new URL(options.authority);
  current = options;
  for (const listener of listeners) {
    listener();
  }
}

export function currentOptions(): KimlikElementsOptions | undefined {
  return current;
}

/** Calls `listener` whenever the components are configured; returns a function that stops it. */
export function onConfigured(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
