# F03 — Role Management

[← Back to E02](../README.md)

---

## Description

Organization admins can create custom roles, assign permissions to each role, and assign roles to users. Default system roles (Admin, Editor, Viewer) are provided out-of-the-box and cannot be deleted or modified.

---

## User Stories

### US-021 — View and manage roles

**As an** Organization Admin, **I want to** see all roles in my organization **so that** I can understand who has what level of access.

**Acceptance Criteria:**

*Happy path*
- [ ] Roles page lists all roles (system + custom) with: name, description, member count, and permission count.
- [ ] System roles are shown with a "System" badge and no edit/delete actions.
- [ ] Clicking a role opens a detail view showing all assigned permissions grouped by module.

*Validation & errors*
- [ ] Users without `roles:read` permission who navigate to this URL are redirected to the home page with a permission error.

*Edge cases*
- [ ] If an org has no custom roles yet, the list shows only the 4 default system roles with a prompt to create a custom role.

*Out of scope*
- Role hierarchy / role inheritance — not in MVP (flat role model only).

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: member count per role requires a JOIN query — not implemented yet (pending API query layer). UI badges and detail view are frontend concerns.

---

### US-022 — Create a custom role

**As an** Organization Admin, **I want to** create a custom role with specific permissions **so that** I can grant exactly the right level of access to a group of users.

**Acceptance Criteria:**

*Happy path*
- [ ] Admin provides a role name and optional description, then selects permissions from a grouped list (grouped by module).
- [ ] Role is created immediately and available for assignment to users.
- [ ] Success message confirms: "Role '{name}' created."

*Validation & errors*
- [ ] Role name: required, 2–50 characters, unique within the org (case-insensitive). Duplicate name shows: "A role with this name already exists."
- [ ] At least one permission must be selected. Submitting with no permissions shows: "A role must have at least one permission."
- [ ] A non-admin calling this API receives HTTP 403.

*Edge cases*
- [ ] Creating two roles with names that differ only in case (e.g., "Manager" and "manager") is blocked.
- [ ] The permissions list is complete and does not require a page reload to reflect newly added platform permissions.

*Out of scope*
- Copying permissions from an existing role as a starting point — not in MVP (user selects permissions manually).

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: 403 permission check requires JWT identity from API layer — pending. Case-insensitive name uniqueness check is done in handler against existing roles in org.
> Decisions: `Role.CreateCustom(name, orgId, permissions[])` factory method; minimum 1 permission enforced in domain.

---

### US-023 — Edit a custom role

**As an** Organization Admin, **I want to** edit an existing custom role **so that** I can adjust permissions as our needs change.

**Acceptance Criteria:**

*Happy path*
- [ ] Admin can modify: name, description, and permissions of any custom role.
- [ ] Changes are saved immediately.
- [ ] Permission changes take effect for affected users on their next API request (their JWT is re-validated against current role permissions).

*Validation & errors*
- [ ] Same name validation rules as create apply.
- [ ] System roles (Admin, Editor, Viewer, End User) cannot be edited; the edit action is not available for them.
- [ ] If the API call fails, the form retains unsaved changes and shows an error.

*Edge cases*
- [ ] Removing a permission from a role affects all users currently holding that role simultaneously.
- [ ] A user whose active session loses a permission they were relying on will receive HTTP 403 on their next request for that resource; the UI re-fetches their permissions and updates accordingly.

*Out of scope*
- Permission change notifications to affected users — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: system role guard (cannot edit Admin/Editor/Viewer/End User) implemented in domain via `IsSystem` flag. 403 check pending API layer.

---

### US-024 — Assign a role to a user

**As an** Organization Admin, **I want to** assign a role to a user **so that** they get the appropriate permissions.

**Acceptance Criteria:**

*Happy path*
- [ ] On the user's profile page, admin can add one or more roles and remove existing ones.
- [ ] Changes take effect on the user's next token refresh (within 15 minutes of the access token TTL).

*Validation & errors*
- [ ] A user must always have at least one role; removing the last role is blocked: "A user must have at least one role."
- [ ] The last user with the Admin role cannot have it removed: "This is the last admin. Assign admin to another user first."
- [ ] A non-admin calling the assign-role API receives HTTP 403.

*Edge cases*
- [ ] If the affected user is currently online, they see a subtle "Your permissions have been updated" banner on their next API response that includes a 403 or on next navigation.
- [ ] A user can hold multiple roles simultaneously; their effective permissions are the union of all role permissions.

*Out of scope*
- Time-limited role assignments (e.g., "grant admin for 24 hours") — not in MVP.

> **Implementation status** — Domain + Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: 403 check pending API layer. "At least one role" guard and "last admin" guard both implemented in handler.
> Decisions: roles stored as `List<Guid>` (`_roleIds`) on `User` aggregate — effective permissions are the union of all assigned roles' permission lists, computed at token issuance time (pending auth layer).
