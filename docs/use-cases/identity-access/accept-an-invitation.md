# Use case — Accept an invitation

> **Navigation**: [← Identity Access](./README.md)

## Purpose

Accept my invitation and set up my account so that I can access the organization.

## Primary actor

- invited user

## Trigger

- User initiates: accept my invitation and set up my account

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
- [ ] Clicking the invitation link opens a page with the organization name shown, prompting the user to set their full name and password.
- [ ] On submit, the account is created, the user is signed in, and they are redirected to the workspace dashboard.

*Validation & errors*
- [ ] Expired invitation link (> 48 hours): "This invitation has expired. Please ask your admin to send a new one."
- [ ] Already-accepted invitation link: "This invitation has already been used. Please sign in."
- [ ] If the invited email already has a platform account (from another org): the user is prompted to sign in with their existing credentials rather than setting a new password.
- [ ] Password must meet the same rules as registration (min 8 chars, letter + number).

*Edge cases*
- [ ] Accepting an invitation on a different device than where the email was opened works correctly.
- [ ] If the inviting admin deactivated the invitation before the user accepted, the link shows: "This invitation has been cancelled."
- [ ] If the org was deleted before the user accepted, the link shows: "This organization no longer exists."

*Out of scope*
- Inviting users who already have accounts on other orgs to join a second org simultaneously — each user belongs to one org in MVP.

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
> **Gaps vs spec:** session sign-in after accept is an API/auth concern, pending.
>
> **Decisions:** expired/accepted/cancelled invitation states enforced in `Invitation.Accept()` domain method, wrapped as `ValidationException` in handler. Platform-wide email check runs after invitation validation — throws `ValidationException` directing user to sign in with existing credentials. `AcceptInvitationHandler` calls `user.VerifyEmail()` — the invitation link proves mailbox ownership (no separate verification email).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| accept-invitation | [source](./wireframes/accept-invitation.excalidraw) | [preview](./wireframes/accept-invitation.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
