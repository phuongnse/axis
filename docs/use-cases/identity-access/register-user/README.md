# Register A Standalone User Account

> **Navigation**: [docs/use-cases/identity-access/README.md](../README.md) · [docs/use-cases/README.md](../../README.md) · [docs/README.md](../../../README.md) · [AGENTS.md](../../../../AGENTS.md)

## Purpose

Register a standalone Axis user with email/password so the user can verify their email and reach the account dashboard.

## Primary actor

- Self-service user

## Trigger

- User opens `/register` without any external setup context.

## Main flow

1. User opens the registration page.
2. User enters first name, last name, email, password, password confirmation, and accepts the current user-level Terms of Service and Privacy Policy.
3. System verifies email uniqueness and password policy.
4. System creates the account, creates the user's personal workspace, records legal acceptance, and sends an email verification link.
5. User opens the verification link.
6. System verifies the email token, signs the user in, completes the browser callback, and routes the user to the dashboard.

## Alternate / error flows

- Duplicate email: show "An account with this email already exists. Sign in instead." as an inline email-field error.
- Invalid, expired, rate-limited, or already-used verification token: show a clear state and allow resend when allowed.
- Server error during submission: show a generic retry message and re-enable the submit button.

## Acceptance Criteria

*Happy path*
- **AC-001** User registration can be started without any team/setup context.
- **AC-002** User can register with name fields, email/password, password confirmation, and current user-level legal acceptance.
- **AC-003** Registration creates the standalone account, personal workspace, and legal acceptance without requiring team/setup context.
- **AC-004** Registration sends an email verification link.
- **AC-005** After successful email verification, the user is signed in and routed to the dashboard.

*Validation & errors*
- **AC-006** Email is required, must be a valid email format, and must be unique across Axis users.
- **AC-007** Password is required, must be 15-128 characters, and common or predictable passwords are rejected.
- **AC-008** Password confirmation must match password exactly.
- **AC-009** Missing team/setup context is accepted for standalone registration.
- **AC-010** Field-level errors are shown inline.
- **AC-011** A 5xx registration response shows a generic retry message and re-enables submit.
- **AC-012** Expired, invalid, rate-limited, and already-used verification links show clear user-facing states.

*Edge cases*
- **AC-013** Multiple rapid submissions are deduplicated with an idempotency key.
- **AC-014** Pasting a password with leading/trailing spaces is accepted as-is.
- **AC-015** Standalone registration leaves the account independent of team/setup context.

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User registers, opens verification email, completes callback, and reaches the dashboard | AC-001, AC-002, AC-004, AC-005, AC-009 | Playwright | Yes |
| AT-002 | E2E | Duplicate email shows the exact inline email-field error | AC-006, AC-010 | Playwright | Yes |
| AT-003 | API | Registration persists account data, personal workspace, legal acceptance, verification token, and no team/setup dependency | AC-003, AC-004, AC-009, AC-015 | xUnit API | Yes |
| AT-004 | Component | Empty form, invalid email, password confirmation, and backend field errors render inline | AC-006, AC-008, AC-010 | Vitest | Yes |
| AT-005 | Component | Password policy rejects short/common passwords and accepts leading/trailing spaces as entered | AC-007, AC-014 | Vitest | Yes |
| AT-006 | Component | 5xx submission failure shows generic retry text and re-enables submit | AC-011 | Vitest | Yes |
| AT-007 | Component/API | Expired, invalid, rate-limited, and already-used verification links show clear states; resend remains available where allowed | AC-012 | Vitest + xUnit API | Yes |
| AT-008 | Application | Completed or in-progress idempotency key deduplicates repeated registration attempts | AC-013 | xUnit Application | Yes |

## Out Of Scope

- Dashboard experience.

## Design System

| Surface | Contract |
|---|---|
| Registration, confirmation, verification, and callback screens | Use existing auth feature consumer contracts, shared UI primitives, semantic tokens, and observable loading/error/disabled states. |

## Design Sources

| Screen | Source | Preview |
|---|---|---|
| registration flow | N/A | N/A |

## Diagrams

### register-user-journey

```mermaid
sequenceDiagram
  actor User
  participant Web as Web App
  participant API as API
  participant Identity as Identity
  participant Mail as SMTP / Maildev
  participant OIDC as OpenIddict

  User->>Web: Open /register
  User->>Web: Submit registration form
  Web->>API: POST /api/users/register
  API->>Identity: Register user
  Identity->>Identity: Create account, personal workspace, legal acceptance, verification token
  Identity->>Mail: Send verification email
  API-->>Web: Check your email
  User->>Web: Open verification link
  Web->>API: POST /api/auth/verify-email
  API->>Identity: Verify token and prepare dashboard access
  API->>OIDC: Create short-lived browser session
  API-->>Web: Session established
  Web->>OIDC: GET /connect/authorize
  OIDC-->>Web: Redirect /callback?code=...
  Web->>OIDC: POST /connect/token
  OIDC-->>Web: Access token
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
> **Implemented:** The standalone registration use case is implemented end to end. `POST /api/users/register` owns submission, account creation, personal workspace creation, legal acceptance, idempotency, and verification email creation; the use case also covers email verification, resend states, post-verification PKCE, and dashboard routing.
>
> **Gaps vs spec:** none for standalone email/password registration.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are automated by Playwright, Vitest, xUnit API, and xUnit Application.
>
> **Decisions:** No editable design-source artifact is committed for this use case; implemented screens are covered by component and E2E tests.
