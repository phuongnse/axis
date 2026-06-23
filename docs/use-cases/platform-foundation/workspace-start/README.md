# Use case - Open workspace start

> **Navigation**: [<- Platform Foundation](../README.md) . [Use cases index](../README.md#use-cases)

## Purpose

Open the first authenticated workspace screen so that the user understands their account state, workspace access, and current plan usage without seeing placeholder operational data.

## Primary actor

- Signed-in user

## Trigger

- User signs in successfully or opens `/dashboard`.

## Main flow

1. SPA route protection confirms an in-memory access token exists.
2. Client loads the current user profile from `GET /api/users/me`.
3. If the profile is linked to a workspace and has `workspace:settings:read`, client loads `GET /api/workspaces/current/settings`.
4. Dashboard renders the current account, workspace state, and real usage values returned by the API.

## Alternate / error flows

- Profile request is loading: show a skeleton rather than a blank screen.
- Profile request fails: show an error message with retry.
- Account has no Workspace: show an honest empty state and do not call Workspace settings.
- Account is linked to a workspace but lacks settings permission: show workspace access as active without exposing settings or fake usage.
- Workspace settings request fails: keep the signed-in account state visible and show a retryable settings error.

## Acceptance Criteria

*Happy path*
- [ ] The dashboard loads the current profile from `/api/users/me`.
- [ ] Users with `workspace:settings:read` see workspace name, status, plan, and usage values from `/api/workspaces/current/settings`.
- [ ] Plan usage cards display users, workflows, and monthly executions using API values only.

*Validation & errors*
- [ ] Loading, empty, and error states are explicit and user-actionable.
- [ ] A profile load failure provides a retry action.
- [ ] A settings load failure does not hide the signed-in account state.

*Edge cases*
- [ ] Accounts without a workspace do not request Workspace settings.
- [ ] Accounts without settings permission do not show placeholder usage or fake operational status.

*Out of scope*
- Creating a workspace from this screen.
- Editing Workspace settings.
- Showing workflow/model/event metrics before those backend endpoints exist.

## Design Sources

The current screen reuses the authenticated app shell and dashboard composition. No new design-source artifact is required for this small state-oriented slice.

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | Done |
> | Frontend | In progress |
>
> **Gaps vs spec:** workspace creation CTA and module-specific metrics are intentionally omitted until those use cases have real backend contracts.
>
> **Deferred follow-ups:** Add workspace-creation CTA and module-specific metrics once their use cases and backend contracts are approved.
>
> **Decisions:** Dashboard is a workspace start surface, not a fake operational console. It may show only account/workspace state and API-backed usage until the workflow/data-model modules ship their own endpoints.
>
