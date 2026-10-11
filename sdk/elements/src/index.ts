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

// Importing the package defines the elements, once, even when two copies of it are loaded.
for (const [name, element] of Object.entries(elements)) {
  if (!customElements.get(name)) {
    customElements.define(name, element);
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "kimlik-organizations": KimlikOrganizations;
    "kimlik-organization-members": KimlikOrganizationMembers;
    "kimlik-organization-settings": KimlikOrganizationSettings;
  }
}
