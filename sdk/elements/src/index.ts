import { KimlikOrganizationMembers } from "./members.js";
import { KimlikOrganizations } from "./organizations.js";
import { KimlikOrganizationSettings } from "./settings.js";

export { KimlikApiError } from "./api.js";
export { configureKimlik, type KimlikElementsOptions } from "./config.js";
export { KimlikOrganizationMembers, KimlikOrganizations, KimlikOrganizationSettings };

const elements = {
  "kimlik-organizations": KimlikOrganizations,
  "kimlik-organization-members": KimlikOrganizationMembers,
  "kimlik-organization-settings": KimlikOrganizationSettings,
};

// Importing the package in a browser defines the elements, once: a second copy of it on the page leaves them as they
// are and shares their configuration. Where there are no custom elements, as on a server, importing does nothing.
if (typeof customElements !== "undefined") {
  for (const [name, element] of Object.entries(elements)) {
    if (!customElements.get(name)) {
      customElements.define(name, element);
    }
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "kimlik-organizations": KimlikOrganizations;
    "kimlik-organization-members": KimlikOrganizationMembers;
    "kimlik-organization-settings": KimlikOrganizationSettings;
  }
}
