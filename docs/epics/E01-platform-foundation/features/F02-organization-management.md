# F02 — Organization Management

[← Back to E01](../README.md)

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-org | [source](../wireframes/settings-org.excalidraw) | [preview](../wireframes/settings-org.svg) |
| settings-org-upload-states | [source](../wireframes/settings-org-upload-states.excalidraw) | [preview](../wireframes/settings-org-upload-states.svg) |
| settings-org-profile-states | [source](../wireframes/settings-org-profile-states.excalidraw) | [preview](../wireframes/settings-org-profile-states.svg) |
| settings-org-usage-error | [source](../wireframes/settings-org-usage-error.excalidraw) | [preview](../wireframes/settings-org-usage-error.svg) |
| settings-org-free-plan | [source](../wireframes/settings-org-free-plan.excalidraw) | [preview](../wireframes/settings-org-free-plan.svg) |
| settings-org-access-denied | [source](../wireframes/settings-org-access-denied.excalidraw) | [preview](../wireframes/settings-org-access-denied.svg) |
| settings-org-deletion-scheduled | [source](../wireframes/settings-org-deletion-scheduled.excalidraw) | [preview](../wireframes/settings-org-deletion-scheduled.svg) |
| settings-org-delete-modal | [source](../wireframes/settings-org-delete-modal.excalidraw) | [preview](../wireframes/settings-org-delete-modal.svg) |
| settings-org-delete-states | [source](../wireframes/settings-org-delete-states.excalidraw) | [preview](../wireframes/settings-org-delete-states.svg) |

---

## Description

Allow organization admins to manage their organization's profile, settings, and basic configuration after initial setup.

---

## User Stories

### US-005 — Update organization profile

**As an** Organization Admin, **I want to** update my organization's name and logo **so that** the platform reflects our brand.

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

### US-006 — View organization settings

**As an** Organization Admin, **I want to** view all organization settings in one place **so that** I have full visibility into the configuration.

**Acceptance Criteria:**

*Happy path*
- [ ] Settings page displays: org name, logo, plan name, current usage stats (workflows used/limit, executions this month/limit, users used/limit), timezone, language, and creation date.
- [ ] Usage stats refresh at most 5 minutes behind real-time.

*Validation & errors*
- [ ] Users without the Admin role who navigate to the Settings URL receive HTTP 403 and are redirected to the home page.
- [ ] If usage stats fail to load, they show "—" with a retry button rather than crashing the page.

*Edge cases*
- [ ] If the org is on the free plan with no limits configured, usage shows actual counts without a denominator (e.g., "12 workflows").

*Out of scope*
- Editing all settings inline on this page — this page is read-only for stats; editing is in sub-sections.

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
> **Gaps vs spec:**
> - Frontend-only: usage retry UI, redirect on 403. **Done (backend):** Redis usage cache TTL ≤ 5 minutes (`PlanLimitRedisCache.UsageStatsMaxStaleness`)
> - existing Admin roles backfilled via `OrganizationSettingsPermissionSeeder`.
>
> **Done:** `GET /api/organizations/current/settings` returns plan name, profile, usage limits, deletion schedule metadata.

---

### US-007 — Delete organization

**As an** Organization Admin, **I want to** permanently delete my organization **so that** all our data is removed from the platform.

**Acceptance Criteria:**

*Happy path*
- [ ] Delete option is in the "Danger Zone" section at the bottom of the Settings page.
- [ ] Clicking "Delete organization" opens a confirmation modal requiring the user to type the organization name exactly.
- [ ] After confirmation, a deletion job is queued and the admin is signed out and redirected to the Axis marketing page.
- [ ] Admin receives a confirmation email stating: "Your organization has been scheduled for deletion. All data will be permanently removed in 30 days."

*Validation & errors*
- [ ] The confirmation input must match the organization name exactly (case-sensitive). Mismatch disables the final delete button.
- [ ] If the deletion job fails to queue, the admin sees an error and the org is not deleted.
- [ ] A non-admin who somehow reaches this endpoint gets HTTP 403.

*Edge cases*
- [ ] During the 30-day grace period, an admin can cancel deletion from a "Your organization is scheduled for deletion" banner shown at the top of the workspace.
- [ ] After the grace period, a background job hard-deletes the schema, all files, and all platform-level records for the org.
- [ ] An org with active running workflow executions at deletion time: running executions are cancelled before schema deletion begins.
- [ ] Attempting to sign in to a deleted org returns "This organization no longer exists."

*Out of scope*
- Data export before deletion — available separately as a future feature.
- Immediate hard delete without grace period — the 30-day window is non-negotiable in MVP.

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
> **Gaps vs spec:**
> - marketing-page redirect + forced sign-out after schedule (Frontend/session)
> - abandon in-flight Wolverine step dispatch beyond execution + form-task cancel
> - cross-module hard-delete steps via RabbitMQ commands when modules are extracted (see `docs/WORKAROUNDS.md#org-hard-delete-modulith-cancellers`).
>
> **Deferred (PR #127 follow-up):**
> - marketing-page redirect + forced sign-out after schedule (Frontend/session)
> - abandon in-flight Wolverine step dispatch beyond execution + form-task cancel
> - cross-module hard-delete steps via RabbitMQ commands when modules are extracted (see `docs/WORKAROUNDS.md#org-hard-delete-modulith-cancellers`).
>
> **Done:**
> - schedule rollback when job queue fails
> - hard-delete cancels executions + pending form tasks, drops tenant schemas, deletes logo S3 object, purges Identity platform rows (users, roles, invitations, provisioning)
> - login returns org-not-found when org row removed.
