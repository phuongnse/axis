# Use case — Update organization profile

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

update my organization's name and logo so that the platform reflects our brand.

## Primary actor

- Organization Admin

## Trigger

- User initiates: update my organization's name and logo

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Allow organization admins to manage their organization's profile, settings, and basic configuration after initial setup.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



**Acceptance Criteria:**

*Happy path*
- [ ] Admin can update: organization name, logo, timezone, and default language from the Settings page.
- [ ] Changes are saved immediately on form submit and reflected in the UI (header, profile) without a full page reload.
- [ ] A success toast confirms the save.

*Validation & errors*
- [ ] Organization name: required, 2–100 characters. Inline error shown if violated.
- [ ] Logo: must be PNG, JPG, or SVG; max 2 MB. Shows an error if the wrong file type or size is uploaded before uploading begins.
- [ ] If the API call fails (network/server error), the form retains the unsaved values and shows an error message; the old data is not lost.
- [ ] Timezone must be a valid IANA timezone string; invalid values are rejected.

*Edge cases*
- [ ] If two admins save conflicting profile changes simultaneously, the last write wins and both see the final state on next load.
- [ ] Uploading a logo while on a slow connection shows an upload progress indicator; navigating away during upload shows a "Changes in progress" warning.
- [ ] Organization name change does not affect the internal schema name (slug), which is immutable after provisioning.

*Out of scope*
- Custom domain / vanity URL — not in MVP.
- White-label theming (custom colors, fonts) — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** Frontend-only AC: toast, upload progress, navigate-away warning. **Done (backend):** language tag validation (`en`, `en-US`).
>
> **Done:** `Organization` profile fields + `UpdateOrganizationProfileCommand`; S3 logo storage; IANA timezone validation.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-org | [source](./wireframes/settings-org.excalidraw) | [preview](./wireframes/settings-org.svg) |
| settings-org-upload-states | [source](./wireframes/settings-org-upload-states.excalidraw) | [preview](./wireframes/settings-org-upload-states.svg) |
| settings-org-profile-states | [source](./wireframes/settings-org-profile-states.excalidraw) | [preview](./wireframes/settings-org-profile-states.svg) |
| settings-org-usage-error | [source](./wireframes/settings-org-usage-error.excalidraw) | [preview](./wireframes/settings-org-usage-error.svg) |
| settings-org-free-plan | [source](./wireframes/settings-org-free-plan.excalidraw) | [preview](./wireframes/settings-org-free-plan.svg) |
| settings-org-access-denied | [source](./wireframes/settings-org-access-denied.excalidraw) | [preview](./wireframes/settings-org-access-denied.svg) |
| settings-org-deletion-scheduled | [source](./wireframes/settings-org-deletion-scheduled.excalidraw) | [preview](./wireframes/settings-org-deletion-scheduled.svg) |
| settings-org-delete-modal | [source](./wireframes/settings-org-delete-modal.excalidraw) | [preview](./wireframes/settings-org-delete-modal.svg) |
| settings-org-delete-states | [source](./wireframes/settings-org-delete-states.excalidraw) | [preview](./wireframes/settings-org-delete-states.svg) |

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
