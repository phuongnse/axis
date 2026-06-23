# Use case — View workspace settings

> **Navigation**: [← Platform Foundation](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

View all workspace settings in one place so that I have full visibility into the configuration.

## Primary actor

- Workspace Admin

## Trigger

- User initiates: view all workspace settings in one place

## Main flow

1. Actor starts the — View workspace settings flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Allow Workspace admins to manage their workspace's profile, settings, and basic configuration after initial setup.

## Acceptance Criteria

*Happy path*
- [ ] Settings page displays: workspace name, logo, plan name, current usage stats (workflows used/limit, executions this month/limit, users used/limit), timezone, language, and creation date.
- [ ] Usage stats refresh at most 5 minutes behind real-time.

*Validation & errors*
- [ ] Users without the Admin role who navigate to the Settings URL receive HTTP 403 and are redirected to the home page.
- [ ] If usage stats fail to load, they show "—" with a retry button rather than crashing the page.

*Edge cases*
- [ ] If the workspace is on the free plan with no limits configured, usage shows actual counts without a denominator (e.g., "12 workflows").

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
> - existing Admin roles backfilled via `WorkspaceSettingsPermissionSeeder`.
>
> **Done:** `GET /api/workspaces/current/settings` returns plan name, profile, usage limits, deletion schedule metadata.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-workspace-usage-error | [source](./settings-workspace-usage-error.excalidraw) | [preview](./settings-workspace-usage-error.svg) |
| settings-workspace-free-plan | [source](./settings-workspace-free-plan.excalidraw) | [preview](./settings-workspace-free-plan.svg) |
| settings-workspace-access-denied | [source](./settings-workspace-access-denied.excalidraw) | [preview](./settings-workspace-access-denied.svg) |
