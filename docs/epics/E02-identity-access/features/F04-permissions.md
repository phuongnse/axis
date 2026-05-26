# F04 — Permission System

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| settings-roles | [source](../wireframes/settings-roles.excalidraw) | [preview](../wireframes/settings-roles.svg) |


[← Back to E02](../README.md)

---

## Description

A resource-based permission system where each permission grants the ability to perform a specific action on a resource type. Permissions are assigned to roles, roles are assigned to users.

---

## Permission Catalogue

| Module | Permission Key | Description |
|---|---|---|
| **Data Modeling** | `data_modeling:model:read` | View model definitions |
| | `data_modeling:model:write` | Create and edit models |
| | `data_modeling:model:delete` | Delete models |
| | `data_modeling:record:read` | View data records |
| | `data_modeling:record:write` | Create and edit records |
| | `data_modeling:record:delete` | Delete records |
| **Workflow Builder** | `workflow:definition:read` | View workflow definitions |
| | `workflow:definition:write` | Create and edit workflows |
| | `workflow:definition:delete` | Delete workflows |
| | `workflow:trigger:manual` | Manually trigger a workflow |
| **Form Builder** | `form:definition:read` | View form definitions |
| | `form:definition:write` | Create and edit forms |
| | `form:submit` | Submit a form (granted per-task at runtime) |
| **Execution** | `execution:read` | View execution history |
| | `execution:cancel` | Cancel a running execution |
| | `execution:retry` | Retry a failed execution |
| **Page Builder** | `page:read` | View pages |
| | `page:write` | Create and edit pages |
| | `page:publish` | Publish/unpublish pages |
| **Users & Roles** | `users:read` | View users list |
| | `users:invite` | Invite users |
| | `users:deactivate` | Deactivate users |
| | `roles:read` | View roles |
| | `roles:write` | Create and edit roles |

---

## Default Role Permissions

| Permission | Admin | Editor | Viewer | End User |
|---|---|---|---|---|
| All `:read` permissions | ✅ | ✅ | ✅ | ❌ |
| All `:write` permissions | ✅ | ✅ | ❌ | ❌ |
| All `:delete` permissions | ✅ | ❌ | ❌ | ❌ |
| `execution:retry` / `cancel` | ✅ | ✅ | ❌ | ❌ |
| `workflow:trigger:manual` | ✅ | ✅ | ❌ | ❌ |
| `users:*` / `roles:*` | ✅ | ❌ | ❌ | ❌ |
| `page:publish` | ✅ | ❌ | ❌ | ❌ |
| `form:submit` | Granted at runtime per task | | | ✅ |

---

## User Stories

### US-025 — Permission enforcement on the API

**As a** platform operator, **I want** every API endpoint to enforce the required permission **so that** unauthorized actions are rejected at the server regardless of what the frontend shows.

**Acceptance Criteria:**

*Happy path*
- [ ] Each endpoint is decorated with a policy attribute specifying the required permission(s).
- [ ] Requests from users who hold the required permission proceed normally.

*Validation & errors*
- [ ] A request without the required permission returns HTTP 403 with body: `{ "error": "forbidden", "required_permission": "workflow:definition:write" }`.
- [ ] A request with an expired or invalid JWT returns HTTP 401 before permission checks run.
- [ ] Missing permission returns 403, not 404 — resource existence is not revealed to unauthorized callers.

*Edge cases*
- [ ] An endpoint requiring multiple permissions (e.g., write + a feature flag) checks all conditions before returning 403.
- [ ] Permission checks are evaluated from the JWT claims at request time; role changes that happened after the JWT was issued take effect only after token refresh.
- [ ] All critical endpoints (write, delete, trigger) are covered by automated permission-enforcement tests in the test suite.

*Out of scope*
- Row-level security (e.g., "user can only edit their own records") — not in MVP; all permission checks are type-level.

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
> **Gaps vs spec:** policy-based authorization middleware, `[RequirePermission]` attribute, and automated permission tests backend polish — see gaps below.
> **Decisions:** permissions are included as a flat array in JWT claims at sign-in time (union of all role permissions); checked via ASP.NET Core custom policy at API layer.

---

### US-026 — Permission enforcement in the frontend

**As a** user, **I want** the UI to hide or disable features I don't have access to **so that** I'm not confused by actions that will fail.

**Acceptance Criteria:**

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
- Per-record UI permissions (e.g., hiding individual table rows) — not in MVP.

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
