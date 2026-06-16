# Use case — Change team account plan (admin override)

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Manually change a team account's plan so that I can support early customers and testing without a billing integration.

## Primary actor

- Platform Admin

## Trigger

- User initiates: manually change a team account's plan

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Define subscription plan tiers with feature limits and enforce those limits at the API level. Billing integration is a separate initiative ([PRODUCT_VISION § Non-goals](../../../PRODUCT_VISION.md#non-goals-platform)) — this feature covers plan definitions and enforcement logic only.

## Acceptance Criteria

*Happy path*
- [ ] Platform Admin dashboard has a "Change plan" action per team account.
- [ ] Selecting a new plan updates the team account's plan immediately; new limits take effect on the next API request.
- [ ] The team account's limit counters in Redis are refreshed after a plan change.

*Validation & errors*
- [ ] Downgrading to a lower plan when the team account already exceeds the new limits: the change is allowed, but existing resources over the limit are not deleted. The team account is flagged as "over limit" and blocked from creating additional resources until they are within the new limits.
- [ ] A non-Platform-Admin cannot reach this endpoint (HTTP 403).

*Edge cases*
- [ ] Plan change is logged in the platform audit log with: who changed, from what plan, to what plan, and at what time.

*Out of scope*
- Team account-initiated plan upgrade — part of the separate billing initiative (requires billing integration).

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ (backend AC) |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** [view-available-plans](../view-plans/) public pricing page UI, static fallback on load failure, "Current plan" badge (Frontend only). No multi-workflow bulk-import API yet — single-workflow import/duplicate enforce +1 before save (bulk workflow import acceptance criteria in [bulk export](../../workflow-builder/bulk-export/) applies when bulk endpoint exists).
>
> **Done (backend):**
> - `GET /api/plans` with limits + `featureFlags` + `isAvailableForNewSignups`
> - signed-in team account on retired plan still sees that plan
> - 402 on create/duplicate/import workflow, invite user, start execution
> - Redis read-through cache + INCR/DECR on mutate + execution key TTL to month-end UTC
> - DB fallback + warning when Redis write/read fails
> - delete workflow decrements cache
> - platform plan change 403 + audit log + cache refresh
> - downgrade over limit blocks new creates via existing usage check (resources not deleted).
>
> **Deferred follow-ups:**
> - atomic check-and-consume for monthly execution starts (race can briefly exceed cap under concurrency)
> - fail-closed when usage counter/Redis unavailable (today logs warning and treats usage as 0).
>
> **Decisions:** `featureFlags` derived from plan slug (no JSON column); `PlatformAdmin:UserIds` config for platform admin plan change.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
