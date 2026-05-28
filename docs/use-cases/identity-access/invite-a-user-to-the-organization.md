# Use case — Invite a user to the organization

> **Navigation**: [← Identity Access](./README.md)

## Purpose

invite a team member by email so that they can join the workspace and start collaborating.

## Primary actor

- Organization Admin

## Trigger

- User initiates: invite a team member by email

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Organization admins can invite new members, manage their accounts, and deactivate users who should no longer have access.

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
- [ ] Admin enters an email address and selects a role for the invited user, then clicks "Send invitation."
- [ ] An invitation email is sent with a unique accept link valid for 48 hours.
- [ ] The new user appears in the Users list with status "Pending."
- [ ] Admin can see the invitation sent date and can cancel or resend it.

*Validation & errors*
- [ ] Email must be a valid email format; invalid format shows an inline error before submission.
- [ ] Inviting an email already belonging to an active member of the org returns: "This user is already a member."
- [ ] Inviting an email that has a pending invitation returns: "An invitation has already been sent to this address." with an option to resend.
- [ ] Role selection is required; submitting without selecting a role shows an inline error.
- [ ] If the email service fails to deliver the invitation, the pending invitation is still created and the admin is shown: "Invitation created, but the email could not be sent. Please resend manually."

*Edge cases*
- [ ] Inviting the same email address after cancelling a previous invitation creates a new invitation (old link is invalidated).
- [ ] The user limit check is performed at invitation time, not at acceptance time. If the org is at its user limit, the invitation is blocked with an upgrade prompt.
- [ ] An admin cannot invite themselves (their own email address).

*Out of scope*
- Bulk invitation via CSV upload — not in MVP.

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
> **Gaps vs spec:** Admin self-invite check not implemented (compare invite email to `ICurrentUser` email).
>
> **Done:** HTTP 402 when user plan limit reached (`InviteUserHandler`, platform-foundation subscription plans).
>
> **Decisions:** existing-member and pending-invitation checks throw `ValidationException` with specific messages matching AC wording.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-users | [source](./wireframes/settings-users.excalidraw) | [preview](./wireframes/settings-users.svg) |
| accept-invitation | [source](./wireframes/accept-invitation.excalidraw) | [preview](./wireframes/accept-invitation.svg) |

[← Back to Identity & Access](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
