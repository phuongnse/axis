# Use case — Update Workspace profile

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Update my Workspace's name and logo so that the platform reflects our brand.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: update my Workspace's name and logo

## Main flow

1. Actor starts the — Update Workspace profile flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow Workspace admins to manage their Workspace's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Admin can update: Workspace name, logo, timezone, and default language from the Settings page.
- [ ] Changes are saved immediately on form submit and reflected in the UI (header, profile) without a full page reload.
- [ ] A success toast confirms the save.

*Validation & errors*
- [ ] Workspace name: required, 2–100 characters. Inline error shown if violated.
- [ ] Logo: must be PNG, JPG, or SVG; max 2 MB. Shows an error if the wrong file type or size is uploaded before uploading begins.
- [ ] If the API call fails (network/server error), the form retains the unsaved values and shows an error message; the old data is not lost.
- [ ] Timezone must be a valid IANA timezone string; invalid values are rejected.

*Edge cases*
- [ ] If two admins save conflicting profile changes simultaneously, the last write wins and both see the final state on next load.
- [ ] Uploading a logo while on a slow connection shows an upload progress indicator; navigating away during upload shows a "Changes in progress" warning.
- [ ] Workspace name change does not affect the internal schema name (slug), which is immutable after provisioning.

*Out of scope*
- Custom domain / vanity URL.
- White-label theming (custom colors, fonts).

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
> **Done:** `Workspace` profile fields + `UpdateWorkspaceProfileCommand`; S3 logo storage; IANA timezone validation.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-workspace-profile-states | [source](./settings-workspace-profile-states.excalidraw) | [preview](./settings-workspace-profile-states.svg) |

