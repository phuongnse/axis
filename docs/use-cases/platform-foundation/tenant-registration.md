# Use Case Group — Tenant Registration & Provisioning

[← Back to Platform Foundation](./README.md)

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

## Description

Self-service registration flow where a new organization signs up and is automatically provisioned with an isolated database schema and a default admin account. No manual intervention from the Axis team is required.

---

## Use Cases

### Use case — Register a new organization

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** prospective customer, **I want to** register my organization on the Axis platform **so that** I can start building workflows for my team.

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

### Use case — Verify email and activate account

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** new admin, **I want to** verify my email address **so that** my account is activated and I can access the platform.

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

### Use case — Automatic tenant provisioning

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** new admin, **I want** my organization's environment to be ready immediately after email verification **so that** I can start using the platform without waiting.

**Acceptance Criteria:**

*Happy path*
- [ ] A dedicated PostgreSQL schema is created within 10 seconds of email verification.
- [ ] All base tables are migrated into the new schema automatically.
- [ ] The registering user is assigned the Admin role within the org.
- [ ] Once provisioning completes, the workspace dashboard is fully functional.

*Validation & errors*
- [ ] If provisioning fails (e.g., DB timeout), the error is logged with full context and a retry job is scheduled automatically (up to 3 retries, with exponential backoff).
- [ ] If provisioning fails after all retries, a platform alert is triggered for the Axis team to investigate.
- [ ] The user is not stuck: the UI shows a "Setting up your workspace…" state and retries polling every 5 seconds.

*Edge cases*
- [ ] Provisioning is idempotent: running it twice for the same org does not create duplicate schemas or tables.
- [ ] If the org schema already exists (partial previous run), the migration runner detects this and continues from where it left off.

*Out of scope*
- Custom schema naming chosen by the user — schema names are auto-generated.

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
> **Gaps vs spec:** provisioning wait UI (email verification) pending Frontend.
>
> **Done:**
> - org enters `Provisioning` on verify
> - per-module `TenantModuleProvisionReportEvent` + Identity coordinator schedules up to 3 retries with exponential backoff
> - critical log alert when exhausted
> - `GET /api/auth/provisioning-status?token=` for polling.
>
> **Deferred (PR #N follow-up):** external paging integration for platform alerts (critical log is the MVP signal).
>
> **Decisions:** provisioning is fully event-driven over Kafka per [ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope) — no central provisioner. The verify endpoint stays fast, the provisioning failure mode is decoupled from email verification, and each module owns its own schema lifecycle (satisfies ADR-010 "extraction is a redeploy"). Tenant schema name is derived from `Organization.Id` as `tenant_{orgId:N}` (32-char hex, no dashes) — stable across the lifetime of the org and safe as a Postgres identifier.

---

### Use case — Select a subscription plan during registration

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As a** new admin, **I want to** choose a subscription plan during registration **so that** I know what features and limits I have access to.

**Acceptance Criteria:**

*Happy path*
- [ ] Available plans are shown in a comparison table before the registration form.
- [ ] A free/trial plan is always available with no payment required.
- [ ] Selected plan is saved to the organization record during provisioning.
- [ ] After activation, the workspace header shows the current plan name.

*Validation & errors*
- [ ] If no plan is explicitly selected, the free/trial plan is applied by default.
- [ ] Feature limits are enforced immediately after provisioning (e.g., creating a 4th workflow on a 3-workflow plan returns HTTP 402 with a clear upgrade message).

*Edge cases*
- [ ] If a paid plan is selected in MVP (before billing integration), it is treated as trial with a flag for the Axis team to follow up.

*Out of scope*
- Credit card collection and payment processing — Phase 2.
- Plan upgrade/downgrade self-service — covered in subscription-plans.

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
> - pricing comparison table and workspace header plan name pending Frontend. **Done (backend):** `POST /api/organizations/` accepts optional `subscriptionPlanId`
> - invalid/unavailable plan ids fall back to Free
> - org stores `subscription_plan_id`
> - subscription-plans enforces limits (402) after provisioning.
>
> **Decisions:** MVP paid plan selection has no billing flag column yet — treat as normal plan assignment until billing Phase 2.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
