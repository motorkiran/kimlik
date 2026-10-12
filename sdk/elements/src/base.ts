import { accountApi, KimlikApiError, type MyOrganization } from "./api.js";
import { currentOptions, onConfigured } from "./config.js";
import { h } from "./dom.js";
import { isMessageKey, languageOf, translate, type MessageKey } from "./i18n.js";
import { styles } from "./styles.js";

// Importing the package where there are no elements, as a server rendering the page does, must not fail.
const Base: typeof HTMLElement = globalThis.HTMLElement ?? (class {} as unknown as typeof HTMLElement);

/**
 * What the components share: a shadow root with their styles, loading once Kimlik is configured and again when the
 * organization changes, one change at a time, an error shown in place, and a live region that announces what changed.
 */
export abstract class KimlikElement extends Base {
  static readonly observedAttributes: string[] = ["organization", "lang"];

  protected readonly root: ShadowRoot;

  // The regions stay the same nodes and only their text changes, which is what screen readers announce.
  readonly #alert = h("p", { role: "alert", part: "error", tabindex: -1, hidden: true });
  readonly #content = h("div");
  readonly #status = h("p", { role: "status", part: "status", tabindex: -1, class: "visually-hidden" });

  #error = "";
  #message = "";
  #connected = false;
  #loaded = false;
  #loading = 0;
  #busy = false;
  #focus: string | undefined;
  #stopListening: (() => void) | undefined;

  constructor() {
    super();
    this.root = this.attachShadow({ mode: "open" });
    this.root.append(h("style", {}, styles), this.#alert, this.#content, this.#status);
  }

  connectedCallback(): void {
    this.#connected = true;
    this.#stopListening = onConfigured(() => void this.#reload({ reset: true }));
    this.render();
    if (currentOptions()) {
      void this.#reload({ reset: true });
    }
  }

  disconnectedCallback(): void {
    this.#connected = false;
    this.#stopListening?.();

    // What is still loading is for a page the element has left.
    this.#loading++;
  }

  attributeChangedCallback(name: string, previous: string | null, value: string | null): void {
    // Attributes set before the element is on the page are read when it gets there.
    if (previous === value || !this.#connected) {
      return;
    }

    if (name !== "organization") {
      this.render();
      return;
    }

    // Another organization: what is shown, typed and said belongs to the one before, and goes at once.
    this.#loaded = false;
    this.#error = "";
    this.#message = "";
    this.render({ reset: true });
    if (currentOptions()) {
      void this.#reload({ reset: true });
    }
  }

  /** Loads what the component shows again, and shows it. */
  refresh(): Promise<void> {
    return this.#reload({});
  }

  /**
   * Fetches what the component shows, and returns a function that takes it into the component. The function runs only
   * when no later load has started meanwhile, so that answers that arrive out of order do not mix.
   */
  protected abstract load(): Promise<() => void>;

  protected abstract view(): Node[];

  protected t(key: MessageKey, ...values: (string | number)[]): string {
    return translate(languageOf(this), key, ...values);
  }

  /**
   * Runs a change, then loads again and announces what `done` says; shows what went wrong instead when it fails. While
   * it runs, the component takes no other change.
   */
  protected act(change: () => Promise<unknown>, done: () => string): Promise<boolean> {
    return this.#one(async () => {
      if (!(await this.#try(change))) {
        return false;
      }

      this.#error = "";
      this.#message = done();
      await this.#reload({ reset: true });
      return true;
    });
  }

  /** Runs a change, and shows what went wrong when it fails; the caller shows what comes of it. */
  protected attempt(change: () => Promise<unknown>): Promise<boolean> {
    return this.#one(() => this.#try(change));
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
    this.#error = "";
    this.render();
  }

  /** Shows the view again after the user opened or closed part of it, with focus on the control with the key. */
  protected show(focus: string): void {
    this.#error = "";
    this.#message = "";
    this.render({ focus });
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

  /**
   * Builds the view again. What the user typed stays, unless `reset` says the view shows something new; focus stays on
   * the control with the same key, or goes to `focus`, or to what is announced when the control is gone.
   */
  protected render(options: { reset?: boolean; focus?: string } = {}): void {
    const focused = options.focus ?? this.#focus ?? this.#focusedKey();
    this.#focus = undefined;
    const typed = options.reset ? undefined : this.#typed();

    const loading = !currentOptions() || (!this.#loaded && !this.#error);
    this.#content.replaceChildren(...(loading ? [h("p", { class: "muted" }, this.t("loading"))] : this.#loaded ? this.view() : []));
    for (const [key, value] of typed ?? []) {
      const input = this.#control<HTMLInputElement>(key);
      if (!input) {
        continue;
      }

      if (typeof value === "boolean") {
        input.checked = value;
      } else {
        input.value = value;
      }
    }

    say(this.#alert, this.#error);
    this.#alert.hidden = !this.#error;
    say(this.#status, this.#message);
    this.#status.classList.toggle("visually-hidden", !this.#message);

    if (focused) {
      (this.#control(focused) ?? (this.#error ? this.#alert : this.#status)).focus();
    }
  }

  async #reload(options: { reset?: boolean; keepError?: boolean }): Promise<void> {
    const loading = ++this.#loading;
    let take: (() => void) | undefined;
    let failure: unknown;
    try {
      take = await this.load();
    } catch (error) {
      failure = error;
    }

    if (loading !== this.#loading) {
      return;
    }

    if (take) {
      take();
      this.#loaded = true;
      this.#error = options.keepError ? this.#error : "";
    } else {
      this.#loaded = false;
      this.#error = this.#messageFor(failure);
      this.#message = "";
    }

    this.render(options.reset ? { reset: true } : {});
  }

  /** Runs one piece of work at a time: a second click, or Enter pressed twice, does nothing while the first is under way. */
  async #one(work: () => Promise<boolean>): Promise<boolean> {
    if (this.#busy) {
      return false;
    }

    this.#busy = true;
    this.#focus = this.#focusedKey();
    for (const button of this.#content.querySelectorAll("button")) {
      button.disabled = true;
    }

    try {
      return await work();
    } finally {
      this.#busy = false;
    }
  }

  async #try(change: () => Promise<unknown>): Promise<boolean> {
    try {
      await change();
      return true;
    } catch (error) {
      this.#error = this.#messageFor(error);
      this.#message = "";

      // "Not found" and "conflict" mean the view is behind what Kimlik has, so it loads again under the message.
      if (error instanceof KimlikApiError && (error.status === 404 || error.status === 409)) {
        await this.#reload({ keepError: true });
      } else {
        this.render();
      }

      return false;
    }
  }

  #control<T extends HTMLElement = HTMLElement>(key: string): T | null {
    return this.#content.querySelector<T>(`[data-key="${CSS.escape(key)}"]`);
  }

  #focusedKey(): string | undefined {
    return (this.root.activeElement as HTMLElement | null)?.dataset["key"];
  }

  /** What the user typed or ticked, by the control's key. */
  #typed(): Map<string, string | boolean> {
    const typed = new Map<string, string | boolean>();
    for (const input of this.#content.querySelectorAll<HTMLInputElement>("input[data-key]")) {
      typed.set(input.dataset["key"]!, input.type === "checkbox" ? input.checked : input.value);
    }

    return typed;
  }

  #messageFor(error: unknown): string {
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

function say(region: HTMLElement, text: string): void {
  if (region.textContent !== text) {
    region.textContent = text;
  }
}
