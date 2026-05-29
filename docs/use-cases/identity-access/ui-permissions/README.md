# Use case — Permission enforcement in the frontend

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

The UI to hide or disable features I don't have access to so that I'm not confused by actions that will fail.

## Primary actor

- user

## Trigger

- User initiates: the UI to hide or disable features I don't have access to

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A resource-based permission system where each permission grants the ability to perform a specific action on a resource type. Permissions are assigned to roles, roles are assigned to users.

## Acceptance Criteria

*Happy path*
- [ ] On sign-in, the client loads the current user's effective permissions and stores them in Zustand.
- [ ] Buttons and menu items for restricted actions are hidden (not just disabled) for users without the required permission.
- [ ] Navigation items for entire sections (e.g., Settings for non-admins) are hidden from the sidebar.

*Validation & errors*
- [ ] If the permissions API call fails on sign-in, the client defaults to the most restrictive view (Viewer-level) and shows a banner: "Could not load your permissions. Some features may be unavailable."
- [ ] Direct URL navigation to a restricted page (e.g., `/settings/roles`) redirects to home with a "You don't have access to this page" toast.

*Edge cases*
- [ ] Frontend permission checks are never treated as the security boundary — the API always re-validates. The UI hiding is UX only.
- [ ] If a user's permissions change mid-session (role edited by admin), the UI reflects the change on the next token refresh (within 15 min) or on the next page navigation that fetches fresh permissions.

*Out of scope*
- Per-record UI permissions (e.g., hiding individual table rows).

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ⏳ |
> | Application | ⏳ |
> | Infrastructure | ⏳ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** all ACs are frontend + API concerns — no Application-layer handler needed. Pending API layer for policy middleware and Frontend for UI hiding logic.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
