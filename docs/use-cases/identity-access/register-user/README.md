# Use case — Register a standalone user account

> **Navigation**: [Identity & Access Management](../README.md) | [Use cases index](../README.md#use-cases)

## Purpose

Register my standalone user identity with email/password so that I can use Axis as an individual before creating or joining a team account.

## Primary actor

- self-service user

## Trigger

- User opens self-service registration without a team account context.

## Main flow

1. User opens the registration page.
2. User enters full name, email, password, password confirmation, and accepts the current user-level Terms of Service / Privacy Policy.
3. System verifies that the user identity is unique and the password satisfies the policy.
4. System creates the standalone user account with no team account membership and sends an email verification link.
5. User opens the verification link.
6. System verifies the user email, establishes the Axis/OpenIddict sign-in session, and routes the user to the dashboard.

## Alternate / error flows

- Email already belongs to another Axis user: show "An account with this email already exists. Sign in instead."
- Verification link expired or already used: show a clear state and allow requesting a new verification email.
- Server error during submission: show a generic retry message and re-enable the submit button.

## Context

This use case owns the smallest complete user identity onboarding path: a person creates a standalone Axis account with email/password, verifies the email address, and reaches their dashboard without any team account context.

Team account membership is intentionally outside this use case. A user can later create or join a team account through team account-specific flows. First-owner setup after [register-team-account](../../platform-foundation/register-team-account/) is a separate setup-token handoff owned by the team account onboarding journey, not by this standalone registration definition.

Third-party identity providers authenticate an individual user and can be linked to that user account in a separate provider-registration/linking use case. They do not prove ownership of a team account; team account onboarding remains in [register-team-account](../../platform-foundation/register-team-account/).

## Acceptance Criteria

*Happy path*
- [ ] User registration can be started without any team account context.
- [ ] User can register with email/password.
- [ ] A standalone registration creates a `User` without requiring a team account membership.
- [ ] Registration sends an email verification link.
- [ ] After successful email verification, the user is signed in through Axis/OpenIddict and redirected to the dashboard.

*Validation & errors*
- [ ] Email: required, valid email format, unique across Axis users.
- [ ] Password path: password is required, minimum 15 characters, max 128 characters, and common or predictable passwords are rejected.
- [ ] Password confirmation must match password exactly.
- [ ] Missing team account context is accepted for standalone registration.
- [ ] All field-level errors are shown inline, not as a global toast.
- [ ] If the API returns a server error (5xx), the form shows a generic "Something went wrong, please try again" message and the submit button re-enables.
- [ ] Expired, invalid, rate-limited, and already-used verification links show clear user-facing states.

*Edge cases*
- [ ] Multiple rapid submissions are deduplicated with an idempotency key.
- [ ] Pasting a password with leading/trailing spaces is accepted as-is.
- [ ] A standalone user can later create or join a team account without re-registering.

*Out of scope*
- Creating a new team account; see [register-team-account](../../platform-foundation/register-team-account/).
- Registering from a first-owner setup token; that handoff belongs to [register-team-account](../../platform-foundation/register-team-account/).
- Invitation-token registration / joining a team account; see [accept-invite](../accept-invite/) and [invite-user](../invite-user/).
- Microsoft / Google / GitHub registration and account linking ([ADR-027](../../../TECH_STACK.md#adr-027-external-identity-providers-for-user-sign-in-and-registration)).
- Enterprise SAML/SCIM federation and per-tenant IdP configuration.
- CAPTCHA / bot protection.

## Wireframes

User registration reuses the auth card system with email/password setup and field-level help text. Team account context is not shown as a required field on the standalone registration screen.

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| register-user | [source](./register-user.excalidraw) | [preview](./register-user.svg) |

## Diagrams

### register-user-journey

```mermaid
sequenceDiagram
  actor User
  participant Web as Web App
  participant API as Identity API
  participant OIDC as OpenIddict

  User->>Web: Open standalone registration
  User->>Web: Submit email + password + legal acceptance
  Web->>API: POST /api/users/register
  API->>API: Create standalone user
  API-->>Web: Check your email
  Web-->>User: Confirmation screen
  User->>Web: Open verification link
  Web->>API: POST /api/auth/verify-email
  API->>OIDC: Establish Axis sign-in session
  API-->>Web: nextStep = Dashboard
  Web-->>User: Open dashboard
```

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | Done |
> | Application | Done |
> | Infrastructure | Done |
> | API | Done |
> | Frontend | Done |
>
> **Implemented:** Email/password standalone registration is implemented at `POST /api/users/register`, including idempotency, current legal-version acceptance, email verification, post-verification PKCE session establishment, confirmation/resend states, and dashboard routing.
>
> **Gaps vs spec:** none for standalone email/password registration.
>
> **Deferred follow-ups:**
> - Microsoft / Google / GitHub providers are a separate provider registration/linking use case.
> - Invitation-token registration belongs to invitation acceptance/join flows.
> - Polished team account setup-token handoff remains with [register-team-account](../../platform-foundation/register-team-account/).
>
> **Decisions:** Providers belong to user identity only; they must never create a team account directly. First-owner setup is part of team account onboarding, not standalone registration.
