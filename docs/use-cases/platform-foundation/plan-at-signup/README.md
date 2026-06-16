# Use case — Select a subscription plan during registration

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Choose a subscription plan during registration so that I know what features and limits I have access to.

## Primary actor

- new admin

## Trigger

- User initiates: choose a subscription plan during registration

## Main flow

1. Actor starts the — Select a subscription plan during registration flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Self-service registration flow where a new tenant signs up and is automatically provisioned with an isolated database schema and a default admin account. No manual intervention from the Axis team is required.

## Acceptance Criteria

*Happy path*
- [ ] Available plans are shown in a comparison table before the registration form.
- [ ] A free/trial plan is always available with no payment required.
- [ ] Selected plan is saved to the tenant record during provisioning.
- [ ] After activation, the workspace header shows the current plan name.

*Validation & errors*
- [ ] If no plan is explicitly selected, the free/trial plan is applied by default.
- [ ] Feature limits are enforced immediately after provisioning (e.g., creating a 4th workflow on a 3-workflow plan returns HTTP 402 with a clear upgrade message).

*Edge cases*
- [ ] If a paid plan is selected before billing integration is live, it is assigned like any other plan (no separate billing flag column yet); payment collection is the billing initiative follow-up.

*Out of scope*
- Credit card collection and payment processing — part of the separate billing initiative.
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
> - pricing comparison table and workspace header plan name pending Frontend. **Done (backend):** `POST /api/tenants/` accepts optional `subscriptionPlanId`
> - invalid/unavailable plan ids fall back to Free
> - tenant stores `subscription_plan_id`
> - subscription-plans enforces limits (402) after provisioning.
>
> **Decisions:** Paid plan selection uses normal `subscription_plan_id` assignment until billing integration; no trial-only flag column.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
