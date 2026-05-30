# Use case — Enforce plan limits at the API

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Enforce subscription plan limits at the API so that organizations cannot exceed their subscription without upgrading.

## Primary actor

- Platform (enforcement on behalf of any tenant user action)

## Trigger

- Any mutating operation that counts against plan limits (workflows, executions, users, etc.).

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.


## Acceptance Criteria

*Happy path*
- [ ] When an org is within limits, all operations proceed normally with no noticeable overhead.
- [ ] Plan limit checks complete in under 10 ms (Redis-cached counters).

*Validation & errors*
- [ ] Creating a workflow beyond the plan's workflow limit returns HTTP 402 with body: `{ "error": "plan_limit_exceeded", "limit_type": "workflows", "current": N, "max": M, "upgrade_url": "..." }`.
- [ ] Triggering an execution when the monthly execution limit is reached returns HTTP 402 with `limit_type: "executions_per_month"`.
- [ ] Inviting a user beyond the user limit returns HTTP 402 with `limit_type: "users"`.
- [ ] The HTTP 402 response always includes a human-readable `message` field in addition to the machine-readable `error` field.

*Edge cases*
- [ ] Monthly execution counters reset at midnight UTC on the 1st of each month; a TTL on the Redis key ensures automatic reset.
- [ ] If Redis is unavailable, the limit check falls back to a DB count query (slower but correct) and logs a warning.
- [ ] Bulk operations (e.g., importing 5 workflows) check the limit against the total before beginning any creation; partial success is not allowed.
- [ ] Deleting a workflow decrements the workflow counter immediately.

*Out of scope*
- Soft limits with grace period (allowing some overage before blocking).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |


> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | N/A |
>
> **Gaps vs spec:** atomic check-and-consume for monthly execution starts under concurrency; fail-closed when Redis unavailable (today logs warning and treats usage as 0).
>
> **Done (backend):** 402 on workflow/user/execution limits; Redis read-through + INCR/DECR; DB fallback; delete workflow decrements counter.
>
> **Deferred (PR #N follow-up):** execution counter race; fail-closed Redis for usage reads.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

