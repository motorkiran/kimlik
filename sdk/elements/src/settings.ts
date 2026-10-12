import { accountApi, KimlikApiError, type MyOrganization } from "./api.js";
import { holds, KimlikElement } from "./base.js";
import { h } from "./dom.js";

const settingsWrite = "kimlik.org.settings:write";

/**
 * `<kimlik-organization-settings organization="…">`: one of the user's organizations, which users who may change its
 * settings rename, give another short name, logo or MFA requirement, and delete once they type its short name. Every
 * member can leave it here, after confirming. Saving fires `kimlik-organization-update` with the organization's `id` and
 * `slug`; leaving fires `kimlik-organization-leave` and deleting `kimlik-organization-delete`, both with its `id`.
 */
export class KimlikOrganizationSettings extends KimlikElement {
  #organization: MyOrganization | undefined;
  #gone: string | undefined;
  #leaving = false;

  protected async load(): Promise<() => void> {
    let organization: MyOrganization;
    try {
      organization = await this.organization();
    } catch (error) {
      // Still gone, as expected after leaving or deleting it: the component goes on saying so.
      if (this.#gone === this.getAttribute("organization") && error instanceof KimlikApiError && error.status === 404) {
        return () => undefined;
      }

      throw error;
    }

    return () => {
      this.#gone = undefined;
      this.#leaving = false;
      this.#organization = organization;
    };
  }

  protected view(): Node[] {
    if (this.#gone !== undefined) {
      return [];
    }

    const organization = this.#organization!;
    const writable = holds(organization, settingsWrite);
    return [...(writable ? [this.settingsForm(organization)] : []), this.leaving(organization), ...(writable ? [this.deletion(organization)] : [])];
  }

  private settingsForm(organization: MyOrganization): Node {
    const name = h("input", { type: "text", name: "name", required: true, maxlength: 100, value: organization.name, "data-key": "name" });
    const slug = h("input", {
      type: "text",
      name: "slug",
      required: true,
      maxlength: 64,
      pattern: "[a-z0-9]+(-[a-z0-9]+)*",
      value: organization.slug,
      "aria-describedby": "slug-hint",
      "data-key": "slug",
    });
    const picture = h("input", { type: "url", name: "pictureUrl", maxlength: 2048, value: organization.pictureUrl ?? "", "data-key": "picture" });
    const requireMfa = h("input", { type: "checkbox", name: "requireMfa", checked: organization.requireMfa, "data-key": "require-mfa" });

    return h(
      "section",
      { part: "settings", "aria-labelledby": "settings-title" },
      h("h2", { id: "settings-title" }, this.t("settings.title")),
      h(
        "form",
        {
          onsubmit: (event) => {
            event.preventDefault();
            void this.save(organization, { name: name.value.trim(), slug: slug.value.trim(), pictureUrl: picture.value.trim() || null, requireMfa: requireMfa.checked });
          },
        },
        h("label", {}, this.t("name"), name),
        h("label", {}, this.t("slug"), slug, h("small", { id: "slug-hint" }, this.t("slugHint"))),
        h("label", {}, this.t("settings.picture"), picture),
        h("label", { class: "check" }, requireMfa, this.t("settings.requireMfa")),
        h("div", {}, h("button", { type: "submit", "data-key": "save" }, this.t("settings.save"))),
      ),
    );
  }

  private leaving(organization: MyOrganization): Node {
    return h(
      "section",
      { part: "leave", "aria-labelledby": "leave-title" },
      h("h2", { id: "leave-title" }, this.t("leave.title")),
      h("p", {}, this.t("leave.text", organization.name)),
      this.#leaving
        ? h(
            "div",
            { class: "row", role: "group", "aria-label": this.t("leave.confirm", organization.name) },
            h("span", {}, this.t("leave.confirm", organization.name)),
            h(
              "span",
              { class: "actions" },
              h(
                "button",
                {
                  type: "button",
                  class: "danger",
                  "data-key": "confirm-leave",
                  onclick: () =>
                    void this.end(organization, () => accountApi.leave(organization.id), "kimlik-organization-leave", this.t("leave.done", organization.name)),
                },
                this.t("leave.submit", organization.name),
              ),
              h("button", { type: "button", class: "secondary", "data-key": "cancel-leave", onclick: () => this.confirmLeaving(false) }, this.t("cancel")),
            ),
          )
        : h("button", { type: "button", class: "secondary", "data-key": "leave", onclick: () => this.confirmLeaving(true) }, this.t("leave.submit", organization.name)),
    );
  }

  private deletion(organization: MyOrganization): Node {
    const confirmation = h("input", { type: "text", name: "confirmation", autocomplete: "off", "data-key": "confirmation" });
    return h(
      "section",
      { part: "delete", "aria-labelledby": "delete-title" },
      h("h2", { id: "delete-title" }, this.t("delete.title")),
      h(
        "form",
        {
          onsubmit: (event) => {
            event.preventDefault();
            if (confirmation.value.trim() !== organization.slug) {
              this.warn(this.t("delete.mismatch"));
              return;
            }

            void this.end(organization, () => accountApi.deleteOrganization(organization.id), "kimlik-organization-delete", this.t("delete.done", organization.name));
          },
        },
        h("p", {}, this.t("delete.text", organization.name, organization.slug)),
        h("label", {}, this.t("slug"), confirmation),
        h("div", {}, h("button", { type: "submit", class: "danger", "data-key": "delete" }, this.t("delete.submit", organization.name))),
      ),
    );
  }

  /** Asks before leaving, with focus on "Cancel", or takes the question away again. */
  private confirmLeaving(asking: boolean): void {
    this.#leaving = asking;
    this.show(asking ? "cancel-leave" : "leave");
  }

  private async save(organization: MyOrganization, changes: { name: string; slug: string; pictureUrl: string | null; requireMfa: boolean }): Promise<void> {
    if (await this.act(() => accountApi.updateOrganization(organization.id, changes), () => this.t("settings.saved", changes.name))) {
      this.fire("kimlik-organization-update", { id: organization.id, slug: changes.slug });
    }
  }

  /** Leaves or deletes the organization; once it is done, the component shows nothing more and tells the page. */
  private async end(organization: MyOrganization, change: () => Promise<unknown>, event: string, done: string): Promise<void> {
    if (await this.attempt(change)) {
      this.#gone = organization.id;
      this.announce(done);
      this.fire(event, { id: organization.id });
    }
  }
}
