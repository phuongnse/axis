# Use case — Delete team account

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Permanently delete my team account so that all our data is removed from the platform.

## Primary actor

- Team account Admin

## Trigger

- User initiates: permanently delete my team account

## Main flow

1. Admin opens the Settings danger zone, chooses Delete team account, and types the team account name exactly in the confirmation modal.
2. System schedules the deletion job, sends the confirmation email, and blocks further normal access while the grace period is active.
3. Admin is signed out or redirected according to the current UI support, and the team account enters the scheduled-for-deletion state.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow team account admins to manage their team account's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Delete option is in the "Danger Zone" section at the bottom of the Settings page.
- [ ] Clicking "Delete team account" opens a confirmation modal requiring the user to type the team account name exactly.
- [ ] After confirmation, a deletion job is queued and the admin is signed out and redirected to the Axis marketing page.
- [ ] Admin receives a confirmation email stating: "Your team account has been scheduled for deletion. All data will be permanently removed in 30 days."

*Validation & errors*
- [ ] The confirmation input must match the team account name exactly (case-sensitive). Mismatch disables the final delete button.
- [ ] If the deletion job fails to queue, the admin sees an error and the team account is not deleted.
- [ ] A non-admin who somehow reaches this endpoint gets HTTP 403.

*Edge cases*
- [ ] During the 30-day grace period, an admin can cancel deletion from a "Your team account is scheduled for deletion" banner shown at the top of the workspace.
- [ ] After the grace period, a background job hard-deletes the schema, all files, and all platform-level records for the team account.
- [ ] A team account with active running workflow executions at deletion time: running executions are cancelled before schema deletion begins.
- [ ] Attempting to sign in to a deleted team account returns "This team account no longer exists."

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
> - cross-module hard-delete steps via RabbitMQ commands when modules are extracted (see `docs/WORKAROUNDS.md#team account-hard-delete-modulith-cancellers`).
>
> **Done:**
> - schedule rollback when job queue fails
> - hard-delete cancels executions + pending form tasks, drops tenant schemas, deletes logo S3 object, purges Identity platform rows (users, roles, invitations, provisioning)
> - login returns team account-not-found when team account row removed.
>
> **Decisions:** Hard delete uses in-process modulith cancellers until modules are extracted; cross-module RabbitMQ hard-delete commands are deferred to the extraction boundary.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-team-account-delete-modal | [source](./settings-team-account-delete-modal.excalidraw) | [preview](./settings-team-account-delete-modal.svg) |
| settings-team-account-delete-states | [source](./settings-team-account-delete-states.excalidraw) | [preview](./settings-team-account-delete-states.svg) |
