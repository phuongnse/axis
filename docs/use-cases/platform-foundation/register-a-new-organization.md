# Use case — Register a new organization

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

register my organization on the Axis platform so that I can start building workflows for my team.

## Primary actor

- prospective customer

## Trigger

- User initiates: register my organization on the Axis platform

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Self-service registration flow where a new organization signs up and is automatically provisioned with an isolated database schema and a default admin account. No manual intervention from the Axis team is required.

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
- [ ] Registration form collects: organization name, admin full name, admin email, password, and password confirmation.
- [ ] On successful submission, a verification email is sent and the user sees a confirmation screen.
- [ ] The confirmation screen tells the user to check their email and does not reveal whether the email already exists.

*Validation & errors*
- [ ] Organization name: required, 2–100 characters.
- [ ] Email: required, valid email format, unique across the platform.
- [ ] Password: required, minimum 8 characters, must contain at least one letter and one number.
- [ ] Password confirmation must match password exactly.
- [ ] All field-level errors are shown inline, not as a global toast.
- [ ] Submitting with an already-registered email shows the same confirmation screen (no information leakage about existing accounts).
- [ ] If the API returns a server error (5xx), the form shows a generic "Something went wrong, please try again" message and the submit button re-enables.

*Edge cases*
- [ ] Multiple rapid submissions of the same form are deduplicated (idempotency key on the request).
- [ ] Pasting a password with leading/trailing spaces is accepted as-is (no silent trimming).
- [ ] Organization name with special characters (e.g., `O'Brien & Co.`) is accepted.

*Out of scope*
- Social/SSO sign-up (Google, GitHub) — not in MVP.
- CAPTCHA — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ✅ |
>
> **Gaps vs spec:** none. `Idempotency-Key` header on `POST /api/organizations/` deduplicates rapid resubmits (Pending/Completed/Failed state).
>
> **Decisions:**
> - duplicate email returns silently without creating anything — matches "same confirmation screen" AC. `RegisterOrganizationCommandValidator` enforces: org name 2–100 chars, valid email, password min 8 chars + letter + number, confirmation match. Org slug auto-generated with uniqueness retry loop
> - BCrypt work factor 12. 4 default system roles seeded atomically in the same transaction.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| register-org | [source](./wireframes/register-org.excalidraw) | [preview](./wireframes/register-org.svg) |
| register-org-states | [source](./wireframes/register-org-states.excalidraw) | [preview](./wireframes/register-org-states.svg) |
| email-confirmation | [source](./wireframes/email-confirmation.excalidraw) | [preview](./wireframes/email-confirmation.svg) |
| verify-email | [source](./wireframes/verify-email.excalidraw) | [preview](./wireframes/verify-email.svg) |
| verify-email-rate-limit | [source](./wireframes/verify-email-rate-limit.excalidraw) | [preview](./wireframes/verify-email-rate-limit.svg) |
| login-unverified (email verification sign-in before verify) | [source](../identity-access/wireframes/login-unverified.excalidraw) | [preview](../identity-access/wireframes/login-unverified.svg) |
| workspace-provisioning | [source](./wireframes/workspace-provisioning.excalidraw) | [preview](./wireframes/workspace-provisioning.svg) |
| pricing | [source](./wireframes/pricing.excalidraw) | [preview](./wireframes/pricing.svg) |

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
