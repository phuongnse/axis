# F04 — Form Submission Handling

> **Wireframe**: [docs/epics/E05-form-builder/wireframes/form-submission.excalidraw](../wireframes/form-submission.excalidraw) · [preview](../wireframes/form-submission.svg)

[← Back to E05](../README.md)

---

## Description

When a workflow reaches a Form step, the engine creates a Form Task and notifies the assignee. The assignee opens a unique link, fills the form, and submits it. The engine then validates and continues the workflow.

---

## User Stories

### US-086 — Receive form assignment notification

**As an** assignee, **I want to** be notified when a form is waiting for my input **so that** I know I have an action to take.

**Acceptance Criteria:**

*Happy path*
- [ ] Assignee receives an email within 60 seconds of the Form Task being created, containing: workflow name, form name, due time (if timeout configured), and a direct link to the form.
- [ ] Assignee also receives an in-app notification (bell icon in the nav) if they are a registered platform user.
- [ ] Email and in-app notifications link to the same unique form task URL.

*Validation & errors*
- [ ] If the email service fails to deliver the notification, the Form Task is still created and the engine continues waiting. The failure is logged in the step's execution detail. The admin can resend the notification manually.
- [ ] If the assignee resolves to a role with no members, the engine marks the step as Failed immediately: "No users found with role '{role_name}'."

*Edge cases*
- [ ] If the assignee is a role, all members of that role receive the notification. The form is completed by the first user to submit it; subsequent attempts by other role members are rejected as duplicate.
- [ ] If the assignee is a deactivated user, the step fails immediately (see US-057 edge case).

*Out of scope*
- Push notifications (mobile) — not in MVP.
- Escalation notifications if the form is not submitted after X hours — not in MVP (timeout causes failure, not escalation).

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: No Application handlers for form task management; email notification dispatch and in-app notification pending E06 + notification infrastructure; role member resolution pending Identity integration.
> Decisions: `FormSubmission.AccessToken` is a `Guid` (unique URL lookup key, not JWT); expiry is enforced via `ExpiresAt` field + `Expire()` domain method. Non-idempotent domain transition — `Expire()` throws if status is not Pending; idempotency is handled at the job/application level (caller checks status before invoking).
> Diagram pending: `FormTaskStatus.Completed` → `Submitted` in form-model diagram (the status name better reflects the action — a form task is "submitted", not "completed"). Update `formModelDiagram()` in `generate-diagrams.mjs`.

---

### US-087 — Open and submit an assigned form

**As an** assignee, **I want to** open the form link and submit my responses **so that** the workflow can continue.

**Acceptance Criteria:**

*Happy path*
- [ ] The form link opens a clean, standalone page (no platform nav, just the form) showing the workflow name, form name, and all form fields.
- [ ] Pre-populated fields (from context expressions) are shown with their values; the assignee can modify them.
- [ ] On successful submission, the page shows: "Thank you! Your response has been recorded. The workflow will continue." The workflow engine resumes automatically.

*Validation & errors*
- [ ] Required fields left empty show inline errors on submit; the form does not close.
- [ ] Field-level validation errors (min/max, format, etc.) are shown inline per field.
- [ ] Attempting to submit when the form task has expired shows: "This form request has expired. Contact your workflow administrator."
- [ ] Attempting to submit a form that has already been submitted shows: "This form has already been submitted."
- [ ] If the submission API call fails (network error), the form retains all entered values and shows a "Submission failed. Please try again." error.

*Edge cases*
- [ ] The form page is accessible without signing in to the Axis platform (the unique URL is the access control mechanism). No login is required.
- [ ] The form link works on mobile browsers with a responsive layout.
- [ ] File Upload fields on the standalone form page: files are uploaded directly to object storage via a pre-signed URL; the form does not need to send the file through the API server.
- [ ] If the assignee opens the form in two browser tabs and submits from one, the other tab shows "already submitted" on the next interaction.

*Out of scope*
- Saving a draft of the form and resuming later — not in MVP.
- The assignee being able to add comments or annotations to the form submission — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: No `SubmitFormCommand` handler; standalone form page (no login required), pre-signed URL file upload, and multi-tab deduplication pending Frontend + API.

---

### US-088 — View pending form tasks

**As an** Organization Member, **I want to** see a list of all form tasks assigned to me **so that** I don't miss any pending actions.

**Acceptance Criteria:**

*Happy path*
- [ ] "My Tasks" page (accessible from the top navigation) lists all pending Form Tasks assigned to the current user.
- [ ] Each task shows: form name, workflow name, assigned at, timeout/due time (if set), and a direct link to the form.
- [ ] Default sort: oldest first (most urgent).
- [ ] A separate "Completed" tab shows submitted tasks.

*Validation & errors*
- [ ] If the tasks list fails to load, an error state with a "Retry" button is shown.

*Edge cases*
- [ ] Tasks assigned by role (where the user is a member of that role) also appear in "My Tasks."
- [ ] A task that was submitted by another role member (and thus no longer pending) disappears from the user's "My Tasks" within 60 seconds (polling or SignalR push).
- [ ] Expired tasks (timed out) appear in a separate "Expired" tab, not in Pending.

*Out of scope*
- Delegating a task to another user — not in MVP.
- Bulk task completion — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: No `GetMyFormTasksQuery` handler; "My Tasks" page, role-assigned task aggregation, and SignalR push updates pending Frontend + API + E06.

---

### US-089 — Handle form step timeout

**As a** workflow designer, **I want to** configure a timeout on a Form step **so that** the workflow doesn't wait indefinitely.

**Acceptance Criteria:**

*Happy path*
- [ ] Timeout is configured in hours (1–720) in the Form step config panel.
- [ ] When the timeout expires, a Wolverine scheduled job marks the Form Task as `Expired` and the step as `Failed`.
- [ ] The workflow failure flow is triggered (error notification sent, execution marked `Failed`).

*Validation & errors*
- [ ] Timeout value must be a positive integer between 1 and 720.
- [ ] A Form step without a timeout configured waits indefinitely (no timeout is a valid configuration).

*Edge cases*
- [ ] If the form is submitted within the timeout window, the scheduled expiry job is cancelled.
- [ ] Expiry jobs are idempotent: if the job fires more than once (at-least-once delivery), the second invocation detects the task is already expired and exits gracefully.
- [ ] If the workflow is cancelled while a Form Task is pending, the task is marked `Cancelled` and the expiry job is cancelled. The form link shows: "This workflow has been cancelled."

*Out of scope*
- Sending a reminder notification before timeout expires — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: Wolverine scheduled expiry job, task expiry marking, and workflow failure flow pending Application + Infrastructure.
