import { accountApi, KimlikApiError, type MyOrganization } from "./api.js";
import { currentOptions, onConfigured } from "./config.js";
import { h } from "./dom.js";
import { isMessageKey, languageOf, translate, type MessageKey } from "./i18n.js";
import { styles } from "./styles.js";

/**
 * What the components share: a shadow root with their styles, loading once Kimlik is configured and again when an
 * attribute changes, an error shown in place, and a live region that announces what changed.
 */
export abstract class KimlikElement extends HTMLElement {
  static readonly observedAttributes: string[] = ["organization", "lang"];

  protected readonly root: ShadowRoot;
  #error: string | undefined;
  #message = "";
  #loaded = false;
  #stopListening: (() => void) | undefined;

  constructor() {
    super();
    this.root = this.attachShadow({ mode: "open" });
  }

  connectedCallback(): void {
    this.#stopListening = onConfigured(() => void this.refresh());
    if (currentOptions()) {
      void this.refresh();
    } else {
      this.render();
    }
  }

  disconnectedCallback(): void {
    this.#stopListening?.();
  }

  attributeChangedCallback(_name: string, previous: string | null, value: string | null): void {
    if (previous !== value && this.isConnected && currentOptions()) {
      void this.refresh();
    }
  }

  /** Loads what the component shows, and shows it. */
  async refresh(): Promise<void> {
    try {
      await this.load();
      this.#loaded = true;
      this.#error = undefined;
    } catch (error) {
      this.#loaded = false;
      this.#error = this.messageFor(error);
    }

    this.render();
  }

  protected abstract load(): Promise<void>;

  protected abstract view(): Node[];

  protected t(key: MessageKey, ...values: (string | number)[]): string {
    return translate(languageOf(this), key, ...values);
  }

  /** Runs a change, then loads again and announces what `done` says; shows what went wrong instead when it fails. */
  protected async act(change: () => Promise<unknown>, done: () => string): Promise<boolean> {
    if (!(await this.attempt(change))) {
      return false;
    }

    this.#message = done();
    await this.refresh();
    return true;
  }

  /** Runs a change, and shows what went wrong when it fails. */
  protected async attempt(change: () => Promise<unknown>): Promise<boolean> {
    try {
      await change();
      return true;
    } catch (error) {
      this.#error = this.messageFor(error);
      this.#message = "";
      this.render();
      return false;
    }
  }

  /** Shows what is wrong with what the user entered, as an alert. */
  protected warn(message: string): void {
    this.#error = message;
    this.#message = "";
    this.render();
  }

  /** Announces a result to screen readers and shows it. */
  protected announce(message: string): void {
    this.#message = message;
    this.#error = undefined;
    this.render();
  }

  /** The user's own organization of the `organization` attribute. */
  protected async organization(): Promise<MyOrganization> {
    const id = this.getAttribute("organization");
    const organization = id ? (await accountApi.organizations()).find((candidate) => candidate.id === id) : undefined;
    if (!organization) {
      throw new KimlikApiError(404, "organization.not_found", "The organization does not exist.");
    }

    return organization;
  }

  /** Fires an event that leaves the shadow root, for the page to react to. */
  protected fire(type: string, detail: unknown): void {
    this.dispatchEvent(new CustomEvent(type, { detail, bubbles: true, composed: true }));
  }

  protected render(): void {
    // Focus stays on the control with the same key when the view is built again.
    const focused = (this.root.activeElement as HTMLElement | null)?.dataset["key"];
    const content = !currentOptions() || (!this.#loaded && !this.#error) ? [h("p", { class: "muted" }, this.t("loading"))] : this.#loaded ? this.view() : [];

    this.root.replaceChildren(
      h("style", {}, styles),
      ...(this.#error ? [h("p", { role: "alert", part: "error" }, this.#error)] : []),
      ...content,
      h("p", { role: "status", class: this.#message ? "" : "visually-hidden", part: "status" }, this.#message),
    );

    if (focused) {
      this.root.querySelector<HTMLElement>(`[data-key="${CSS.escape(focused)}"]`)?.focus();
    }
  }

  private messageFor(error: unknown): string {
    if (error instanceof KimlikApiError) {
      if (error.status === 401) {
        return this.t("error.session");
      }

      return isMessageKey(error.code) ? this.t(error.code) : error.message;
    }

    return this.t("error.generic");
  }
}

/** Whether the user holds the permission in the organization. */
export function holds(organization: MyOrganization, permission: string): boolean {
  return organization.permissions.includes(permission);
}
