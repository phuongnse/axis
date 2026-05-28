# Use case — Delete organization

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Permanently delete my organization so that all our data is removed from the platform.

## Primary actor

- Organization Admin

## Trigger

- User initiates: permanently delete my organization

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow organization admins to manage their organization's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

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
> **Deferred (PR #127 follow-up):**
> - marketing-page redirect + forced sign-out after schedule (Frontend/session)
> - abandon in-flight Wolverine step dispatch beyond execution + form-task cancel
> - cross-module hard-delete steps via RabbitMQ commands when modules are extracted (see `docs/WORKAROUNDS.md#org-hard-delete-modulith-cancellers`).
>
> **Done:**
> - schedule rollback when job queue fails
> - hard-delete cancels executions + pending form tasks, drops tenant schemas, deletes logo S3 object, purges Identity platform rows (users, roles, invitations, provisioning)
> - login returns org-not-found when org row removed.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-org-delete-modal | [source](./settings-org-delete-modal.excalidraw) | [preview](./settings-org-delete-modal.svg) |
| settings-org-delete-states | [source](./settings-org-delete-states.excalidraw) | [preview](./settings-org-delete-states.svg) |
| settings-org-deletion-scheduled | [source](./settings-org-deletion-scheduled.excalidraw) | [preview](./settings-org-deletion-scheduled.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
