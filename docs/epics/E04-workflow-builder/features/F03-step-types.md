# F03 — Step Type Configuration

> **Wireframe**: [docs/wireframes/E04-workflow-builder/workflow-editor.excalidraw](../../../wireframes/E04-workflow-builder/workflow-editor.excalidraw) · [preview](../../../wireframes/E04-workflow-builder/workflow-editor.svg)

[← Back to E04](../README.md)

---

## Description

Each step has a type that determines what it does when executed. Users configure each step type through the canvas side panel.

---

## US-057 — Configure a Form step

**As an** Organization Member, **I want to** configure a Form step with a specific form and assignee **so that** the right person receives the form during execution.

**Acceptance Criteria:**

*Happy path*
- [ ] Form picker shows a searchable list of all forms in the org.
- [ ] Assignee field accepts: a specific user (by name/email search), a role name (all members of that role are notified), or a context expression like `{{context.step_id.submitted_by}}`.
- [ ] Optional timeout field accepts a number of hours (1–720). When set, the step auto-fails if not submitted within that period.
- [ ] Step node on the canvas shows the selected form name and assignee as a summary.

*Validation & errors*
- [ ] Saving without selecting a form is blocked: "A form must be selected."
- [ ] Saving without setting an assignee is blocked: "An assignee is required."
- [ ] An invalid context expression in the assignee field (e.g., mismatched braces) shows: "Invalid expression syntax."
- [ ] If the selected form is deleted after the step is configured, the step node shows a broken indicator and publishing is blocked.

*Edge cases*
- [ ] If the assignee resolves to a deactivated user at execution time, the engine falls back to notifying all Admins and logs a warning.
- [ ] A timeout of 0 hours is invalid and blocked.

*Out of scope*
- Multiple assignees on a single Form step (assign to all and wait for the first response) — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: form picker UI, assignee expression evaluation, and timeout enforcement pending Frontend + E06.
> Decisions: step config (formId, assignee, timeout) stored as JSONB dict in `steps` column.

---

## US-058 — Configure an HTTP Request step

**As an** Organization Member, **I want to** configure an HTTP Request step **so that** my workflow can integrate with external services.

**Acceptance Criteria:**

*Happy path*
- [ ] Config panel has fields for: method (GET/POST/PUT/PATCH/DELETE), URL, headers (key-value list), body (JSON editor for POST/PUT/PATCH), auth (None / Bearer Token / Basic / API Key), output variable name, and timeout (default 30s, max 300s).
- [ ] URL, header values, and body values support `{{context.var}}` expression interpolation.
- [ ] A "Test request" button sends a real request with the current config (substituting sample values for context expressions) and shows the response status and body in-panel.

*Validation & errors*
- [ ] URL field: required, must be a valid URL (including protocol). Expression-interpolated URLs are validated for format before the `{{` characters.
- [ ] Timeout: must be 1–300 seconds.
- [ ] Auth — Bearer Token: token field is required. Basic Auth: username and password are required.
- [ ] Auth credentials are stored encrypted at rest; they are never returned in GET responses for the workflow definition (replaced with `[REDACTED]`).

*Edge cases*
- [ ] A "Test request" that returns a non-2xx response is shown in the panel as a warning, not an error — it is informational only and does not affect the step config.
- [ ] Response body larger than 1 MB is truncated with a warning at execution time; the truncated value is stored in context.
- [ ] Redirect responses (3xx): the client follows up to 5 redirects by default; configurable.

*Out of scope*
- GraphQL or gRPC step types — not in MVP.
- Response streaming — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: HTTP execution and Test request button pending E06 + Frontend; credential storage redaction is enforced at export (keys matching token/api_key/secret/password/authorization/etc. replaced with `[REDACTED]` in `ExportWorkflowHandler`).

---

## US-059 — Configure a Condition step

**As an** Organization Member, **I want to** add a Condition step **so that** my workflow can take different paths based on data values.

**Acceptance Criteria:**

*Happy path*
- [ ] Expression builder UI (no raw code) supports: field comparisons (`==`, `!=`, `<`, `>`, `<=`, `>=`, `contains`, `starts with`, `ends with`, `is empty`, `is not empty`) and logical operators (AND, OR, NOT).
- [ ] Left-hand side of comparison is a context variable picker (shows all variables available at this step's position in the workflow).
- [ ] Each branch has a label (editable) shown on the canvas edge.
- [ ] A "Default" branch (no condition) catches all unmatched cases; at most one default branch per step.

*Validation & errors*
- [ ] Publishing is blocked if a Condition step has fewer than 2 outgoing branches.
- [ ] Publishing is blocked if a Condition step has no Default branch and the conditions may not be exhaustive (non-exhaustive detection is best-effort; a warning is shown, not a hard block).
- [ ] An invalid expression (e.g., comparing a number field with `contains`) shows: "This operator is not valid for the '{field}' type."

*Edge cases*
- [ ] Branch order matters: branches are evaluated top-to-bottom; the first match wins. The canvas side panel shows branches in their evaluation order with drag-to-reorder.
- [ ] A Condition step with only a Default branch (no other conditions) is valid but the canvas shows a warning: "All inputs will follow the default branch."

*Out of scope*
- Raw expression editing (writing code directly) — the visual builder is the only interface in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: expression builder UI and branch evaluation pending Frontend + E06; condition branches stored in step config JSONB.

---

## US-060 — Configure a Script step

**As an** Organization Member, **I want to** write a small script step **so that** I can transform data that isn't possible with standard steps.

**Acceptance Criteria:**

*Happy path*
- [ ] Script editor (Monaco) has syntax highlighting and basic type hints for the `context` and `output` objects.
- [ ] The `context` object provides read access to all current execution context variables.
- [ ] Writing to `output` (e.g., `output.full_name = context.first_name + " " + context.last_name`) merges those values into the execution context after the step completes.
- [ ] A "Run test" button executes the script with a sample context (user-editable JSON) and shows the resulting `output` in the panel.

*Validation & errors*
- [ ] Timeout: required, 1–60 seconds.
- [ ] Script exceeding the timeout is forcibly terminated; the step is marked Failed with the message "Script execution timed out."
- [ ] Script that throws an unhandled exception marks the step as Failed and stores the exception message and stack trace in the step's error details.
- [ ] Script that attempts forbidden operations (network calls, file access, `process.*`, `require()`/`import`) throws a sandbox violation error immediately.

*Edge cases*
- [ ] `context` is a read-only proxy; attempting to write to `context` directly (not `output`) has no effect and does not throw an error.
- [ ] A script that runs within the timeout but produces no `output` writes is valid; context is unchanged.

*Out of scope*
- Importing external npm packages — not in MVP.
- Python or other language scripts — JavaScript only in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ✅ | Frontend: ⏳
> Gaps vs spec: JS sandbox execution, timeout enforcement, and "Run test" button pending E06 + Frontend.

---

## US-061 — Configure a Notification step

**As an** Organization Member, **I want to** add a Notification step **so that** stakeholders are informed when a workflow reaches a certain point.

**Acceptance Criteria:**

*Happy path*
- [ ] Channel options: Email or Webhook.
- [ ] Email config: recipient(s) (comma-separated emails or context expressions), subject, and body (rich text with `{{expression}}` placeholders).
- [ ] Webhook config: URL (supports expressions), HTTP method (POST only in MVP), and an optional JSON payload template.
- [ ] The step does not pause execution; the workflow continues immediately after the notification is dispatched (fire-and-forget).

*Validation & errors*
- [ ] Email recipient: at least one recipient required. Invalid email format shown inline.
- [ ] Webhook URL: required, must be a valid HTTPS URL. HTTP (non-SSL) URLs are blocked for security.
- [ ] Subject: required for email channel, max 200 characters.

*Edge cases*
- [ ] A failed email delivery (SMTP error, invalid address) logs a warning in the execution step detail but does NOT fail the workflow by default. This behavior is configurable per step: a "Fail workflow on notification error" toggle can be enabled.
- [ ] A webhook notification that times out (> 10s) or returns non-2xx follows the same configurable behavior.
- [ ] Expression placeholders that resolve to `null` or undefined are rendered as an empty string in the notification body.

*Out of scope*
- SMS, Slack, or Teams notification channels — not in MVP.
- Notification templates shared across workflows — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ⚠️ | Frontend: ⏳
> Gaps vs spec: email/webhook dispatch pending E06; configurable fail-on-error toggle not yet implemented in API layer.
