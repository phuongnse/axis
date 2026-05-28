# Use case — View organization settings

> **Navigation**: [← Platform Foundation](./README.md)

## Purpose

View all organization settings in one place so that I have full visibility into the configuration.

## Primary actor

- Organization Admin

## Trigger

- User initiates: view all organization settings in one place

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow organization admins to manage their organization's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Settings page displays: org name, logo, plan name, current usage stats (workflows used/limit, executions this month/limit, users used/limit), timezone, language, and creation date.
- [ ] Usage stats refresh at most 5 minutes behind real-time.

*Validation & errors*
- [ ] Users without the Admin role who navigate to the Settings URL receive HTTP 403 and are redirected to the home page.
- [ ] If usage stats fail to load, they show "—" with a retry button rather than crashing the page.

*Edge cases*
- [ ] If the org is on the free plan with no limits configured, usage shows actual counts without a denominator (e.g., "12 workflows").

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
> - existing Admin roles backfilled via `OrganizationSettingsPermissionSeeder`.
>
> **Done:** `GET /api/organizations/current/settings` returns plan name, profile, usage limits, deletion schedule metadata.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-org | [source](./wireframes/settings-org.excalidraw) | [preview](./wireframes/settings-org.svg) |
| settings-org-usage-error | [source](./wireframes/settings-org-usage-error.excalidraw) | [preview](./wireframes/settings-org-usage-error.svg) |
| settings-org-free-plan | [source](./wireframes/settings-org-free-plan.excalidraw) | [preview](./wireframes/settings-org-free-plan.svg) |
| settings-org-access-denied | [source](./wireframes/settings-org-access-denied.excalidraw) | [preview](./wireframes/settings-org-access-denied.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
