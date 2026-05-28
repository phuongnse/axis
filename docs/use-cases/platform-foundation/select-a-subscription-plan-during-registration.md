# Use case — Select a subscription plan during registration

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

choose a subscription plan during registration so that I know what features and limits I have access to.

## Primary actor

- new admin

## Trigger

- User initiates: choose a subscription plan during registration

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
