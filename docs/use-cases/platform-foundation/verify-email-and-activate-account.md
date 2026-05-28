# Use case — Verify email and activate account

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

verify my email address so that my account is activated and I can access the platform.

## Primary actor

- new admin

## Trigger

- User initiates: verify my email address

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
- [ ] Verification email arrives within 60 seconds of registration.
- [ ] Clicking the verification link activates the account and automatically signs the user in.
- [ ] User is redirected to the workspace after activation (or to the provisioning wait screen if schema is still being created).

*Validation & errors*
- [ ] Verification link is valid for 24 hours; clicking an expired link shows a clear message with a "Resend verification email" button.
- [ ] Clicking an already-used verification link shows "This link has already been used. Please sign in."
- [ ] Clicking a tampered/invalid token shows "Invalid verification link."

*Edge cases*
- [ ] Resend is rate-limited: max 3 resend requests per email per hour; subsequent attempts show "Please wait before requesting another email."
- [ ] If the user tries to sign in before verifying, they see "Please verify your email first" with a resend option.
- [ ] Verification link works regardless of which browser or device it is opened on.

*Out of scope*
- Automatic re-send after X minutes — not in MVP.

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
> **Gaps vs spec:** auto sign-in after verification click pending Frontend.
>
> **Done:**
> - opaque one-time verification tokens in `email_verification_tokens` (SHA-256 hash at rest, 24h TTL, invalidate on verify/resend)
> - resend rate limit 3/email/hour via `IResendVerificationRateLimiter` (Redis atomic INCR+EXPIRE per hashed email, HTTP 429 + message)
> - login returns "Please verify your email" when unverified
> - `GET /api/auth/provisioning-status?token=` accepts the same link token after verify (including used tokens within TTL). IP-level `auth` limiter applies to `/connect/login` and Identity gRPC only — not on verify/resend (avoids starving integration tests).
>
> **Deferred (PR #125 follow-up):** Frontend verify-email flow, provisioning wait screen, and post-verify auto sign-in (email verification).
>
> **Decisions:** `ResendVerificationEmailCommand` silently succeeds for unknown/already-verified emails (no info leakage).

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
