# F02 — User Management

[← Back to E02](../README.md)

---

## Description

Organization admins can invite new members, manage their accounts, and deactivate users who should no longer have access.

---

## User Stories

### US-017 — Invite a user to the organization

**As an** Organization Admin, **I want to** invite a team member by email **so that** they can join the workspace and start collaborating.

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: user limit check not implemented (no subscription/plan concept yet — pending E01 F04). Admin self-invite check not implemented — pending API layer (requires current user identity from JWT).
> Decisions: existing-member and pending-invitation checks throw `ValidationException` with specific messages matching AC wording.

---

### US-018 — Accept an invitation

**As an** invited user, **I want to** accept my invitation and set up my account **so that** I can access the organization.

**Acceptance Criteria:**

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

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: session sign-in after accept is an API/auth concern, pending.
> Decisions: expired/accepted/cancelled invitation states enforced in `Invitation.Accept()` domain method, wrapped as `ValidationException` in handler. Platform-wide email check runs after invitation validation — throws `ValidationException` directing user to sign in with existing credentials.

---

### US-019 — Deactivate a user

**As an** Organization Admin, **I want to** deactivate a user **so that** they can no longer access the workspace without deleting their history.

**Acceptance Criteria:**

*Happy path*
- [ ] Admin clicks "Deactivate" on a user in the Users list and confirms in a dialog.
- [ ] The user's active sessions are invalidated within 60 seconds (refresh tokens revoked, access tokens blacklisted).
- [ ] Deactivated user appears in the Users list with a "Deactivated" badge.
- [ ] Admin can reactivate the user at any time with a "Reactivate" action.

*Validation & errors*
- [ ] An admin cannot deactivate themselves.
- [ ] Deactivating the last Admin-role user is blocked: "You cannot deactivate the last admin of the organization."
- [ ] A non-admin who calls the deactivate API endpoint receives HTTP 403.

*Edge cases*
- [ ] Deactivated user's created content (workflows, models, records) is preserved and attributed to them.
- [ ] A deactivated user who tries to sign in sees: "Your account has been deactivated. Contact your organization admin."
- [ ] A deactivated user with pending form tasks: those tasks are marked "Assignee deactivated" and the admin is notified.

*Out of scope*
- Transferring ownership of content from a deactivated user — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: session revocation (refresh token revoke + access token blacklist) not implemented — pending auth infrastructure (OpenIddict + Redis). Self-deactivation guard and 403 check require current user identity from JWT — pending API layer. Deactivated-user sign-in message handled at auth layer (pending).
> Decisions: "last admin" check queries `CountAdminsAsync` in the repository before deactivating — domain enforces via `ApplicationException` if violated.

---

### US-020 — Manage user profile

**As a** user, **I want to** update my profile information **so that** my name and contact details are current.

**Acceptance Criteria:**

*Happy path*
- [ ] User can update: full name and avatar image from the Profile settings page.
- [ ] Changes are saved immediately and reflected throughout the UI (top nav, comments, assignments).

*Validation & errors*
- [ ] Full name: required, 2–100 characters.
- [ ] Avatar: PNG or JPG only, max 1 MB. Shows an error before upload begins if type or size is invalid.
- [ ] Attempting to change email redirects to a separate flow (email change requires re-verification — see F05).

*Edge cases*
- [ ] Uploading a new avatar replaces the old one; the old file is deleted from storage.
- [ ] If avatar upload fails mid-way, the old avatar remains unchanged and an error is shown.

*Out of scope*
- Public profile visibility — all profiles are private within the org in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: avatar upload (S3) not wired — `UpdateUserProfileHandler` updates name only; avatar URL stored as nullable string, upload flow pending API layer. Email change flow (F05) not started.
> Decisions: name update is a direct property mutation on `User` aggregate with a `UserProfileUpdatedEvent`.
