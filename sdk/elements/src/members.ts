import { accountApi, isOrganizationSystemPermission, listLimit, type Invitation, type Listing, type Member, type MyOrganization, type Role } from "./api.js";
import { holds, KimlikElement } from "./base.js";
import { currentOptions } from "./config.js";
import { h } from "./dom.js";
import { localeOf } from "./i18n.js";

const membersRead = "kimlik.org.members:read";
const membersWrite = "kimlik.org.members:write";

/**
 * `<kimlik-organization-members organization="…">`: the members of one of the user's organizations and the pending
 * invitations. Users who may manage members invite people, send invitations again, revoke them, change members' roles
 * and remove members here, within the roles they hold; the API checks every change again.
 */
export class KimlikOrganizationMembers extends KimlikElement {
  #organization: MyOrganization | undefined;
  #members: Listing<Member> = { items: [], truncated: false };
  #roles: Role[] = [];
  #invitations: Invitation[] = [];
  #me: string | undefined;
  #editing: string | undefined;
  #removing: string | undefined;

  protected async load(): Promise<() => void> {
    const organization = await this.organization();
    let members: Listing<Member> = { items: [], truncated: false };
    let roles: Role[] = [];
    let invitations: Invitation[] = [];
    let me: string | undefined;
    if (holds(organization, membersRead)) {
      [members, roles, invitations, me] = await Promise.all([
        accountApi.members(organization.id),
        accountApi.roles(organization.id),
        accountApi.invitations(organization.id),
        signedInUser(),
      ]);
    }

    return () => {
      this.#organization = organization;
      this.#members = members;
      this.#roles = roles;
      this.#invitations = invitations;
      this.#me = me;
      this.#editing = undefined;
      this.#removing = undefined;
    };
  }

  protected view(): Node[] {
    // Every change below names the organization that is shown, whatever the attribute says by the time it is made.
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
        h("ul", {}, this.#members.items.map((member) => this.member(organization, member, writable))),
        this.#members.truncated ? h("p", { class: "muted" }, this.t("members.truncated", listLimit)) : null,
      ),
      ...(writable ? [this.inviteForm(organization)] : []),
      this.pending(organization, writable),
    ];
  }

  private member(organization: MyOrganization, member: Member, writable: boolean): Node {
    const label = member.name ?? member.email ?? member.userId;
    const roleNames = member.roles.map((key) => this.#roles.find((role) => role.key === key)?.name ?? key);
    const manageable = writable && member.userId !== this.#me && this.canManage(organization, member);
    const open = this.#editing === member.userId || this.#removing === member.userId;

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
        manageable && !open
          ? h(
              "span",
              { class: "actions" },
              h(
                "button",
                {
                  type: "button",
                  class: "secondary",
                  "data-key": `edit-${member.userId}`,
                  "aria-label": this.t("members.changeRolesOf", label),
                  onclick: () => this.open(() => (this.#editing = member.userId), `save-${member.userId}`),
                },
                this.t("members.changeRoles"),
              ),
              h(
                "button",
                {
                  type: "button",
                  class: "secondary",
                  "data-key": `remove-${member.userId}`,
                  "aria-label": this.t("members.removeNamed", label),
                  // Focus goes to "Cancel", so that Enter pressed twice does not remove anyone.
                  onclick: () => this.open(() => (this.#removing = member.userId), `cancel-${member.userId}`),
                },
                this.t("members.remove"),
              ),
            )
          : null,
      ),
      this.#editing === member.userId ? this.rolesForm(organization, member, label) : null,
      this.#removing === member.userId ? this.removal(organization, member, label) : null,
    );
  }

  private rolesForm(organization: MyOrganization, member: Member, label: string): Node {
    const checkboxes = this.assignableRoles(organization).map((role) =>
      h("input", { type: "checkbox", value: role.key, checked: member.roles.includes(role.key), "data-key": `role-${member.userId}-${role.key}` }),
    );
    return h(
      "form",
      {
        onsubmit: (event) => {
          event.preventDefault();
          const chosen = checkboxes.filter((box) => box.checked).map((box) => box.value);
          void this.act(
            () => accountApi.setRoles(organization.id, member.userId, chosen),
            () => this.t("members.rolesSaved", label),
          );
        },
      },
      this.roleChoices(this.t("members.rolesOf", label), checkboxes),
      h(
        "div",
        { class: "actions" },
        h("button", { type: "submit", "data-key": `save-${member.userId}` }, this.t("save")),
        h(
          "button",
          { type: "button", class: "secondary", "data-key": `cancel-${member.userId}`, onclick: () => this.open(() => undefined, `edit-${member.userId}`) },
          this.t("cancel"),
        ),
      ),
    );
  }

  private removal(organization: MyOrganization, member: Member, label: string): Node {
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
            onclick: () =>
              void this.act(
                () => accountApi.removeMember(organization.id, member.userId),
                () => this.t("members.removed", label),
              ),
          },
          this.t("members.remove"),
        ),
        h(
          "button",
          { type: "button", class: "secondary", "data-key": `cancel-${member.userId}`, onclick: () => this.open(() => undefined, `remove-${member.userId}`) },
          this.t("cancel"),
        ),
      ),
    );
  }

  private inviteForm(organization: MyOrganization): Node {
    const email = h("input", { type: "email", name: "email", required: true, maxlength: 256, autocomplete: "off", "data-key": "invite-email" });
    const checkboxes = this.assignableRoles(organization).map((role) => h("input", { type: "checkbox", value: role.key, "data-key": `invite-role-${role.key}` }));
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
            void this.act(
              () => accountApi.invite(organization.id, address, roles),
              () => this.t("invite.sent", address),
            );
          },
        },
        h("label", {}, this.t("email"), email),
        checkboxes.length > 0 ? this.roleChoices(this.t("roles"), checkboxes) : null,
        h("div", {}, h("button", { type: "submit", "data-key": "invite" }, this.t("invite.submit"))),
      ),
    );
  }

  private pending(organization: MyOrganization, writable: boolean): Node {
    const dates = new Intl.DateTimeFormat(localeOf(this), { dateStyle: "medium" });
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
                h("span", {}, h("strong", {}, invitation.email), h("br"), h("small", {}, this.t("pending.expires", dates.format(new Date(invitation.expiresAt))))),
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
                          "aria-label": this.t("pending.resendTo", invitation.email),
                          onclick: () =>
                            void this.act(
                              () => accountApi.resend(organization.id, invitation.id),
                              () => this.t("pending.resent", invitation.email),
                            ),
                        },
                        this.t("pending.resend"),
                      ),
                      h(
                        "button",
                        {
                          type: "button",
                          class: "secondary",
                          "data-key": `revoke-${invitation.id}`,
                          "aria-label": this.t("pending.revokeFor", invitation.email),
                          onclick: () =>
                            void this.act(
                              () => accountApi.revoke(organization.id, invitation.id),
                              () => this.t("pending.revoked", invitation.email),
                            ),
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
  private assignableRoles(organization: MyOrganization): Role[] {
    return this.#roles.filter((role) => role.permissions.filter(isOrganizationSystemPermission).every((permission) => holds(organization, permission)));
  }

  /**
   * Whether the member holds no organization system permission that the user lacks: then every role the member has is
   * one the user may give, so the roles form shows them all.
   */
  private canManage(organization: MyOrganization, member: Member): boolean {
    const assignable = this.assignableRoles(organization);
    return member.roles.every((key) => assignable.some((role) => role.key === key));
  }

  /** Opens or closes a member's roles form or removal, one at a time, and puts focus where the user goes on. */
  private open(change: () => void, focus: string): void {
    this.#editing = undefined;
    this.#removing = undefined;
    change();
    this.show(focus);
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
