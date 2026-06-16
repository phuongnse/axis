# Use case — View and manage roles

> **Navigation**: [← Identity Access](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See all roles in my Tenant so that I can understand who has what level of access.

## Primary actor

- Tenant Admin

## Trigger

- User initiates: see all roles in my Tenant

## Main flow

1. Actor starts the — View and manage roles flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Tenant admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

## Acceptance Criteria

*Happy path*
- [ ] Roles page lists all roles (system + custom) with: name, description, member count, and permission count.
- [ ] System roles are shown with a "System" badge and no edit/delete actions.
- [ ] Clicking a role opens a detail view showing all assigned permissions grouped by module.

*Validation & errors*
- [ ] Users without `roles:read` permission who navigate to this URL are redirected to the home page with a permission error.

*Edge cases*
- [ ] If a tenant has no custom roles yet, the list shows only the 4 default system roles with a prompt to create a custom role.

*Out of scope*
- Role hierarchy / role inheritance — flat role model only.

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
> **Gaps vs spec:** member count per role requires a JOIN query — not implemented yet (query projection pending). UI badges and detail view are frontend concerns.
>
> **Gaps vs spec:**
> - N/A
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

