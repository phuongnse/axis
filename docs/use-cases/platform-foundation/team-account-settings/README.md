# Use case — View team account settings

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

View all team account settings in one place so that I have full visibility into the configuration.

## Primary actor

- Team account Admin

## Trigger

- User initiates: view all team account settings in one place

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow team account admins to manage their team account's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Settings page displays: team account name, logo, plan name, current usage stats (workflows used/limit, executions this month/limit, users used/limit), timezone, language, and creation date.
- [ ] Usage stats refresh at most 5 minutes behind real-time.

*Validation & errors*
- [ ] Users without the Admin role who navigate to the Settings URL receive HTTP 403 and are redirected to the home page.
- [ ] If usage stats fail to load, they show "—" with a retry button rather than crashing the page.

*Edge cases*
- [ ] If the team account is on the free plan with no limits configured, usage shows actual counts without a denominator (e.g., "12 workflows").

*Out of scope*
- Editing all settings inline on this page — this page is read-only for stats; editing is in sub-sections.

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
> - Frontend-only: usage retry UI, redirect on 403. **Done (backend):** Redis usage cache TTL ≤ 5 minutes (`PlanLimitRedisCache.UsageStatsMaxStaleness`)
> - existing Admin roles backfilled via `Team accountSettingsPermissionSeeder`.
>
> **Done:** `GET /api/team-accounts/current/settings` returns plan name, profile, usage limits, deletion schedule metadata.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-team-account-usage-error | [source](./settings-team-account-usage-error.excalidraw) | [preview](./settings-team-account-usage-error.svg) |
| settings-team-account-free-plan | [source](./settings-team-account-free-plan.excalidraw) | [preview](./settings-team-account-free-plan.svg) |
| settings-team-account-access-denied | [source](./settings-team-account-access-denied.excalidraw) | [preview](./settings-team-account-access-denied.svg) |

