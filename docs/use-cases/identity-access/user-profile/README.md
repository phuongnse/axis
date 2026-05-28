# Use case — Manage user profile

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Update my profile information so that my name and contact details are current.

## Primary actor

- user

## Trigger

- User initiates: update my profile information

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Organization admins can invite new members, manage their accounts, and deactivate users who should no longer have access.

## Acceptance Criteria

*Happy path*
- [ ] User can update: full name and avatar image from the Profile settings page.
- [ ] Changes are saved immediately and reflected throughout the UI (top nav, comments, assignments).

*Validation & errors*
- [ ] Full name: required, 2–100 characters.
- [ ] Avatar: PNG or JPG only, max 1 MB. Shows an error before upload begins if type or size is invalid.
- [ ] Attempting to change email redirects to a separate flow (email change requires re-verification — see password-security).

*Edge cases*
- [ ] Uploading a new avatar replaces the old one; the old file is deleted from storage.
- [ ] If avatar upload fails mid-way, the old avatar remains unchanged and an error is shown.

*Out of scope*
- Public profile visibility — all profiles are private within the org in MVP.

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
> **Gaps vs spec:** email change flow (password-security) not started.
>
> **Decisions:** name update is a direct property mutation on `User` aggregate with a `UserProfileUpdatedEvent`. Avatar upload fully wired in `UpdateUserProfileHandler` — validates type (PNG/JPG only) and size (max 1 MB), uploads to S3, deletes old file on replacement.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-users | [source](./wireframes/settings-users.excalidraw) | [preview](./wireframes/settings-users.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
