# Use case — Register a new organization

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Register my organization on the Axis platform — with email/password or a Microsoft, Google, or GitHub account — so that I can start building workflows for my team.

## Primary actor

- prospective customer

## Trigger

- User initiates: register my organization on the Axis platform

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Self-service registration flow where a new organization signs up and is automatically provisioned with an isolated database schema and a default admin account. No manual intervention from the Axis team is required.

## Acceptance Criteria

*Happy path*
- [ ] Registration form collects: organization name, admin full name, admin email, password, and password confirmation.
- [ ] The user must accept the Terms of Service and Privacy Policy (linked) before the form can be submitted; the accepted version is recorded with the account.
- [ ] An organization slug is auto-generated from the organization name (uniqueness-checked) and shown to the user.
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

*External identity providers*
- [ ] Registration page offers **Microsoft**, **Google**, and **GitHub** sign-up alongside the email/password form ([ADR-027](../../../TECH_STACK.md#adr-027-external-identity-providers-for-sign-in-and-registration)).
- [ ] Signing up with a provider runs Authorization Code + PKCE through OpenIddict; the verified email and display name returned by the provider seed the admin account (no password is set).
- [ ] Terms of Service and Privacy Policy acceptance is still required on the provider sign-up path before the organization is created.
- [ ] If the provider's email already belongs to an Axis account, registration is rejected with "An account with this email already exists. Sign in instead." (no duplicate org).
- [ ] A provider that returns no verified email cannot complete registration.

*Out of scope*
- CAPTCHA / bot protection on the registration form.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ⚠️ |
> | Application | ⚠️ |
> | Infrastructure | ⚠️ |
> | API | ⚠️ |
> | Frontend | ⚠️ |
>
> **Gaps vs spec:** Email/password registration is complete — `Idempotency-Key` header on `POST /api/organizations/` deduplicates rapid resubmits (Pending/Completed/Failed state), slug auto-generated with uniqueness retry. **Not yet implemented:** Terms of Service / Privacy Policy acceptance (recording accepted version on the account) and external-provider sign-up (Microsoft/Google/GitHub, ADR-027) — no provider registration in OpenIddict, no provider buttons on the registration page.
>
> **Deferred (PR #146 follow-up):** CAPTCHA/bot protection on the registration form.
>
> **Decisions:**
> - duplicate email returns silently without creating anything — matches "same confirmation screen" AC. `RegisterOrganizationCommandValidator` enforces: org name 2–100 chars, valid email, password min 8 chars + letter + number, confirmation match. Org slug auto-generated with uniqueness retry loop
> - BCrypt work factor 12. 4 default system roles seeded atomically in the same transaction.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| register-org | [source](./register-org.excalidraw) | [preview](./register-org.svg) |
| register-org-states | [source](./register-org-states.excalidraw) | [preview](./register-org-states.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
