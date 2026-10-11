import { accountApi, type MyInvitation, type MyOrganization } from "./api.js";
import { KimlikElement } from "./base.js";
import { h } from "./dom.js";

/**
 * `<kimlik-organizations>`: the user's organizations and the invitations to them, which they accept or decline there.
 * With `allow-create`, users create organizations too, where the installation lets them. Choosing an organization fires
 * `kimlik-organization-select` with its `id` and `slug`.
 */
export class KimlikOrganizations extends KimlikElement {
  static override readonly observedAttributes = ["allow-create", "lang"];

  #organizations: MyOrganization[] = [];
  #invitations: MyInvitation[] = [];

  protected async load(): Promise<void> {
    [this.#organizations, this.#invitations] = await Promise.all([accountApi.organizations(), accountApi.invitationsToMe()]);
  }

  protected view(): Node[] {
    return [
      ...(this.#invitations.length > 0 ? [this.invitations()] : []),
      h(
        "section",
        { part: "organizations", "aria-labelledby": "organizations-title" },
        h("h2", { id: "organizations-title" }, this.t("organizations.title")),
        this.#organizations.length === 0
          ? h("p", { class: "muted" }, this.t("organizations.empty"))
          : h(
              "ul",
              {},
              this.#organizations.map((organization) =>
                h(
                  "li",
                  {},
                  h(
                    "button",
                    {
                      type: "button",
                      class: "link",
                      part: "organization",
                      "data-key": `select-${organization.id}`,
                      onclick: () => this.fire("kimlik-organization-select", { id: organization.id, slug: organization.slug }),
                    },
                    h("strong", {}, organization.name),
                    " ",
                    h("small", {}, organization.slug),
                    h("br"),
                    h("small", {}, this.t("organizations.yourRoles", organization.roles.join(", ") || this.t("noRoles"))),
                  ),
                ),
              ),
            ),
      ),
      ...(this.hasAttribute("allow-create") ? [this.createForm()] : []),
    ];
  }

  private invitations(): Node {
    return h(
      "section",
      { part: "invitations", "aria-labelledby": "invitations-title" },
      h("h2", { id: "invitations-title" }, this.t("invitations.title")),
      h(
        "ul",
        {},
        this.#invitations.map((invitation) =>
          h(
            "li",
            { class: "row" },
            h("span", {}, this.t("invitations.from", invitation.organizationName)),
            h(
              "span",
              { class: "actions" },
              h(
                "button",
                {
                  type: "button",
                  "data-key": `accept-${invitation.id}`,
                  onclick: () =>
                    void this.act(() => accountApi.accept(invitation.id), () => this.t("invitations.accepted", invitation.organizationName)),
                },
                this.t("invitations.accept"),
              ),
              h(
                "button",
                {
                  type: "button",
                  class: "secondary",
                  "data-key": `decline-${invitation.id}`,
                  onclick: () =>
                    void this.act(() => accountApi.decline(invitation.id), () => this.t("invitations.declined", invitation.organizationName)),
                },
                this.t("invitations.decline"),
              ),
            ),
          ),
        ),
      ),
    );
  }

  private createForm(): Node {
    const name = h("input", { type: "text", name: "name", required: true, maxlength: 100, autocomplete: "organization", "data-key": "create-name" });
    const slug = h("input", {
      type: "text",
      name: "slug",
      required: true,
      maxlength: 64,
      pattern: "[a-z0-9]+(-[a-z0-9]+)*",
      "aria-describedby": "slug-hint",
      "data-key": "create-slug",
    });

    return h(
      "section",
      { part: "create", "aria-labelledby": "create-title" },
      h("h2", { id: "create-title" }, this.t("create.title")),
      h(
        "form",
        {
          onsubmit: (event) => {
            event.preventDefault();
            const created = { name: name.value.trim(), slug: slug.value.trim() };
            void this.act(() => accountApi.createOrganization(created.name, created.slug), () => this.t("create.done", created.name));
          },
        },
        h("label", {}, this.t("name"), name),
        h("label", {}, this.t("slug"), slug, h("small", { id: "slug-hint" }, this.t("slugHint"))),
        h("div", {}, h("button", { type: "submit", "data-key": "create" }, this.t("create.submit"))),
      ),
    );
  }
}
