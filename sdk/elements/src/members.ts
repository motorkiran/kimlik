import { accountApi, isOrganizationSystemPermission, type Invitation, type Member, type MyOrganization, type Role } from "./api.js";
import { holds, KimlikElement } from "./base.js";
import { currentOptions } from "./config.js";
import { h } from "./dom.js";
import { languageOf } from "./i18n.js";

const membersRead = "kimlik.org.members:read";
const membersWrite = "kimlik.org.members:write";

/**
 * `<kimlik-organization-members organization="…">`: the members of one of the user's organizations and the pending
 * invitations. Users who may manage members invite people, send invitations again, revoke them, change members' roles
 * and remove members here, within the roles they hold; the API checks every change again.
 */
export class KimlikOrganizationMembers extends KimlikElement {
  #organization: MyOrganization | undefined;
  #members: Member[] = [];
  #roles: Role[] = [];
  #invitations: Invitation[] = [];
  #me: string | undefined;
  #editing: string | undefined;
  #removing: string | undefined;

  protected async load(): Promise<void> {
    const organization = await this.organization();
    this.#organization = organization;
    this.#members = [];
    this.#roles = [];
    this.#invitations = [];
    this.#editing = undefined;
    this.#removing = undefined;
    if (!holds(organization, membersRead)) {
      return;
    }

    [this.#members, this.#roles, this.#invitations, this.#me] = await Promise.all([
      accountApi.members(organization.id),
      accountApi.roles(organization.id),
      accountApi.invitations(organization.id),
      signedInUser(),
    ]);
  }

  protected view(): Node[] {
    const organization = this.#organization!;
    if (!holds(organization, membersRead)) {
      return [h("p", { role: "alert" }, this.t("organization.missing_permission"))];
    }

    const writable = holds(organization, membersWrite);
    return [
      h(
        "section",
        { part: "members", "aria-labelledby": "members-title" },
        h("h2", { id: "members-title" }, this.t("members.title")),
        h("ul", {}, this.#members.map((member) => this.member(member, writable))),
      ),
      ...(writable ? [this.inviteForm()] : []),
      this.pending(writable),
    ];
  }

  private member(member: Member, writable: boolean): Node {
    const label = member.name ?? member.email ?? member.userId;
    const roleNames = member.roles.map((key) => this.#roles.find((role) => role.key === key)?.name ?? key);
    const manageable = writable && member.userId !== this.#me && this.canManage(member);

    return h(
      "li",
      { part: "member" },
      h(
        "div",
        { class: "row" },
        h(
          "span",
          {},
          h("strong", {}, label),
          member.userId === this.#me ? ` ${this.t("you")}` : "",
          member.name && member.email ? [h("br"), h("small", {}, member.email)] : [],
          h("br"),
          h("small", {}, roleNames.join(", ") || this.t("noRoles")),
        ),
        manageable && this.#editing !== member.userId && this.#removing !== member.userId
          ? h(
              "span",
              { class: "actions" },
              h("button", { type: "button", class: "secondary", "data-key": `edit-${member.userId}`, onclick: () => this.show(() => (this.#editing = member.userId)) }, this.t("members.changeRoles")),
              h("button", { type: "button", class: "secondary", "data-key": `remove-${member.userId}`, onclick: () => this.show(() => (this.#removing = member.userId)) }, this.t("members.remove")),
            )
          : null,
      ),
      this.#editing === member.userId ? this.rolesForm(member, label) : null,
      this.#removing === member.userId ? this.removal(member, label) : null,
    );
  }

  private rolesForm(member: Member, label: string): Node {
    const checkboxes = this.assignableRoles().map((role) => h("input", { type: "checkbox", value: role.key, checked: member.roles.includes(role.key) }));
    return h(
      "form",
      {
        onsubmit: (event) => {
          event.preventDefault();
          // Roles the user cannot give stay as they are.
          const kept = member.roles.filter((key) => !this.assignableRoles().some((role) => role.key === key));
          const chosen = checkboxes.filter((box) => box.checked).map((box) => box.value);
          void this.act(() => accountApi.setRoles(this.#organization!.id, member.userId, [...kept, ...chosen]), () => this.t("members.rolesSaved", label));
        },
      },
      this.roleChoices(this.t("members.rolesOf", label), checkboxes),
      h(
        "div",
        { class: "actions" },
        h("button", { type: "submit", "data-key": `save-${member.userId}` }, this.t("save")),
        h("button", { type: "button", class: "secondary", onclick: () => this.show(() => (this.#editing = undefined)) }, this.t("cancel")),
      ),
    );
  }

  private removal(member: Member, label: string): Node {
    return h(
      "div",
      { class: "row", role: "group", "aria-label": this.t("members.confirmRemove", label) },
      h("span", {}, this.t("members.confirmRemove", label)),
      h(
        "span",
        { class: "actions" },
        h(
          "button",
          {
            type: "button",
            class: "danger",
            "data-key": `confirm-remove-${member.userId}`,
            onclick: () => void this.act(() => accountApi.removeMember(this.#organization!.id, member.userId), () => this.t("members.removed", label)),
          },
          this.t("members.remove"),
        ),
        h("button", { type: "button", class: "secondary", onclick: () => this.show(() => (this.#removing = undefined)) }, this.t("cancel")),
      ),
    );
  }

  private inviteForm(): Node {
    const email = h("input", { type: "email", name: "email", required: true, maxlength: 256, autocomplete: "off", "data-key": "invite-email" });
    const checkboxes = this.assignableRoles().map((role) => h("input", { type: "checkbox", value: role.key }));
    return h(
      "section",
      { part: "invite", "aria-labelledby": "invite-title" },
      h("h2", { id: "invite-title" }, this.t("invite.title")),
      h(
        "form",
        {
          onsubmit: (event) => {
            event.preventDefault();
            const address = email.value.trim();
            const roles = checkboxes.filter((box) => box.checked).map((box) => box.value);
            void this.act(() => accountApi.invite(this.#organization!.id, address, roles), () => this.t("invite.sent", address));
          },
        },
        h("label", {}, this.t("email"), email),
        checkboxes.length > 0 ? this.roleChoices(this.t("roles"), checkboxes) : null,
        h("div", {}, h("button", { type: "submit", "data-key": "invite" }, this.t("invite.submit"))),
      ),
    );
  }

  private pending(writable: boolean): Node {
    const dates = new Intl.DateTimeFormat(languageOf(this), { dateStyle: "medium" });
    return h(
      "section",
      { part: "invitations", "aria-labelledby": "pending-title" },
      h("h2", { id: "pending-title" }, this.t("pending.title")),
      this.#invitations.length === 0
        ? h("p", { class: "muted" }, this.t("pending.empty"))
        : h(
            "ul",
            {},
            this.#invitations.map((invitation) =>
              h(
                "li",
                { class: "row", part: "invitation" },
                h(
                  "span",
                  {},
                  h("strong", {}, invitation.email),
                  h("br"),
                  h("small", {}, this.t("pending.expires", dates.format(new Date(invitation.expiresAt)))),
                ),
                writable
                  ? h(
                      "span",
                      { class: "actions" },
                      h(
                        "button",
                        {
                          type: "button",
                          class: "secondary",
                          "data-key": `resend-${invitation.id}`,
                          onclick: () => void this.act(() => accountApi.resend(this.#organization!.id, invitation.id), () => this.t("pending.resent", invitation.email)),
                        },
                        this.t("pending.resend"),
                      ),
                      h(
                        "button",
                        {
                          type: "button",
                          class: "secondary",
                          "data-key": `revoke-${invitation.id}`,
                          onclick: () => void this.act(() => accountApi.revoke(this.#organization!.id, invitation.id), () => this.t("pending.revoked", invitation.email)),
                        },
                        this.t("pending.revoke"),
                      ),
                    )
                  : null,
              ),
            ),
          ),
    );
  }

  private roleChoices(legend: string, checkboxes: HTMLInputElement[]): Node {
    return h(
      "fieldset",
      {},
      h("legend", {}, legend),
      checkboxes.map((box) => {
        const role = this.#roles.find((candidate) => candidate.key === box.value)!;
        return h("label", {}, box, role.name, role.description ? h("small", {}, ` — ${role.description}`) : null);
      }),
    );
  }

  /** Roles whose organization system permissions the user holds, which they may give. */
  private assignableRoles(): Role[] {
    return this.#roles.filter((role) => role.permissions.filter(isOrganizationSystemPermission).every((permission) => holds(this.#organization!, permission)));
  }

  /** Whether the member holds no organization system permission that the user lacks. */
  private canManage(member: Member): boolean {
    return member.roles.every((key) => {
      const role = this.#roles.find((candidate) => candidate.key === key);
      return role !== undefined && role.permissions.filter(isOrganizationSystemPermission).every((permission) => holds(this.#organization!, permission));
    });
  }

  private show(change: () => void): void {
    this.#editing = undefined;
    this.#removing = undefined;
    change();
    this.render();
  }
}

/** The subject of the access token, so that the user's own row offers no removal; leaving is in the settings. */
async function signedInUser(): Promise<string | undefined> {
  try {
    const token = await currentOptions()!.accessToken();
    const payload = token.split(".")[1];
    if (!payload) {
      return undefined;
    }

    const json = atob(payload.replace(/-/g, "+").replace(/_/g, "/"));
    const subject = (JSON.parse(json) as { sub?: unknown }).sub;
    return typeof subject === "string" ? subject : undefined;
  } catch {
    return undefined;
  }
}
