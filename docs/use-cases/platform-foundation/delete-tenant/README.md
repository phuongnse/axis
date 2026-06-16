# Use case — Delete Tenant

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Permanently delete my Tenant so that all our data is removed from the platform.

## Primary actor

- Tenant Admin

## Trigger

- User initiates: permanently delete my Tenant

## Main flow

1. Admin opens the Settings danger zone, chooses Delete Tenant, and types the Tenant name exactly in the confirmation modal.
2. System schedules the deletion job, sends the confirmation email, and blocks further normal access while the grace period is active.
3. Admin is signed out or redirected according to the current UI support, and the Tenant enters the scheduled-for-deletion state.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow Tenant admins to manage their Tenant's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Delete option is in the "Danger Zone" section at the bottom of the Settings page.
- [ ] Clicking "Delete Tenant" opens a confirmation modal requiring the user to type the Tenant name exactly.
- [ ] After confirmation, a deletion job is queued and the admin is signed out and redirected to the Axis marketing page.
- [ ] Admin receives a confirmation email stating: "Your Tenant has been scheduled for deletion. All data will be permanently removed in 30 days."

*Validation & errors*
- [ ] The confirmation input must match the Tenant name exactly (case-sensitive). Mismatch disables the final delete button.
- [ ] If the deletion job fails to queue, the admin sees an error and the tenant is not deleted.
- [ ] A non-admin who somehow reaches this endpoint gets HTTP 403.

*Edge cases*
- [ ] During the 30-day grace period, an admin can cancel deletion from a "Your Tenant is scheduled for deletion" banner shown at the top of the workspace.
- [ ] After the grace period, a background job hard-deletes the schema, all files, and all platform-level records for the tenant.
- [ ] a tenant with active running workflow executions at deletion time: running executions are cancelled before schema deletion begins.
- [ ] Attempting to sign in to a deleted tenant returns "This Tenant no longer exists."

*Out of scope*
- Data export before deletion — available separately as a future feature.
- Immediate hard delete without grace period — the 30-day window is non-negotiable.

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
> - Frontend: marketing-page redirect + forced sign-out after schedule.
> - Infrastructure: abandon in-flight Wolverine step dispatch beyond execution + form-task cancel.
>
> **Deferred follow-ups:**
> - cross-module hard-delete steps via RabbitMQ commands when modules are extracted (see `docs/WORKAROUNDS.md#tenant-hard-delete-modulith-cancellers`).
>
> **Done:**
> - schedule rollback when job queue fails
> - hard-delete cancels executions + pending form tasks, drops tenant schemas, deletes logo S3 object, purges Identity platform rows (users, roles, invitations, provisioning)
> - login returns tenant-not-found when tenant row removed.
>
> **Decisions:** Hard delete uses in-process modulith cancellers until modules are extracted; cross-module RabbitMQ hard-delete commands are deferred to the extraction boundary.
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-tenant-delete-modal | [source](./settings-tenant-delete-modal.excalidraw) | [preview](./settings-tenant-delete-modal.svg) |
| settings-tenant-delete-states | [source](./settings-tenant-delete-states.excalidraw) | [preview](./settings-tenant-delete-states.svg) |
