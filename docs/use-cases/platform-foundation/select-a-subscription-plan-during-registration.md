# Use case — Select a subscription plan during registration

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

Choose a subscription plan during registration so that I know what features and limits I have access to.

## Primary actor

- new admin

## Trigger

- User initiates: choose a subscription plan during registration

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| pricing | [source](./wireframes/pricing.excalidraw) | [preview](./wireframes/pricing.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
