export interface KimlikElementsOptions {
  /** Kimlik's address, such as `https://id.example.com/`. */
  authority: string | URL;
  /**
   * Returns a current access token for the `kimlik.organizations` scope, such as one from your OpenID Connect library.
   * It is called before every request, so it can renew the token when it is about to expire. Ask for this token on its
   * own: one that also carries your API's scope could be used on the user's organizations by whoever receives it there.
   */
  accessToken: () => string | Promise<string>;
  /** The fetch to call Kimlik with; the global one by default. */
  fetch?: typeof fetch;
}

interface State {
  options?: KimlikElementsOptions;
  readonly listeners: Set<() => void>;
}

// Kept on the global object, so that two copies of the package on one page, as bundles can end up with, share it.
const key = Symbol.for("kimlik.elements");
const scope = globalThis as unknown as Record<symbol, State | undefined>;
const state: State = (scope[key] ??= { listeners: new Set() });

/** Tells the components where Kimlik is and how to get an access token; they load once it is called. */
export function configureKimlik(options: KimlikElementsOptions): void {
  new URL(options.authority);
  state.options = options;
  for (const listener of state.listeners) {
    listener();
  }
}

export function currentOptions(): KimlikElementsOptions | undefined {
  return state.options;
}

/** Calls `listener` whenever the components are configured; returns a function that stops it. */
export function onConfigured(listener: () => void): () => void {
  state.listeners.add(listener);
  return () => state.listeners.delete(listener);
}
