# Use case — Configure a Schedule trigger

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Schedule a workflow so that it runs automatically at defined intervals.

## Primary actor

- Organization Member

## Trigger

- Schedule a workflow.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A workflow must have at least one trigger before it can be published. Triggers define how and when a workflow execution starts.

## Acceptance Criteria

*Happy path*
- [ ] Cron expression input field with a human-readable preview below it (e.g., "Every Monday at 9:00 AM UTC").
- [ ] Timezone selector (IANA timezone list, searchable) defaults to the organization's configured timezone.
- [ ] "Max concurrent runs" field (default: 1) controls how many executions of this workflow may run at the same time.
- [ ] Schedule is registered with Wolverine on workflow publish; deregistered on archive.

*Validation & errors*
- [ ] Invalid cron expression shows: "Invalid cron expression. Example: `0 9 * * 1` (every Monday at 9 AM)."
- [ ] Cron with a frequency of less than every 5 minutes is blocked: "Minimum schedule interval is 5 minutes."
- [ ] An invalid or missing timezone shows: "Please select a valid timezone."

*Edge cases*
- [ ] If the previous scheduled run is still in progress when the next cron tick fires and `max_concurrent_runs = 1`, the new run is skipped and a warning is logged (not an error).
- [ ] If `max_concurrent_runs > 1`, all concurrent executions proceed independently.
- [ ] Changing the cron expression of an active workflow updates the schedule immediately without archiving and re-publishing.

*Out of scope*
- Date-specific one-time scheduling (e.g., "run once on 2026-12-25").

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
> - Wolverine cron job registration on publish and deregistration on archive pending workflow-engine
> - cron expression validation (min 5-min interval, IANA timezone) pending.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

