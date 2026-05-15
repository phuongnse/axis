# F04 — Trigger Configuration

[← Back to E04](../README.md)

---

## Description

A workflow must have at least one trigger before it can be published. Triggers define how and when a workflow execution starts.

---

## US-062 — Configure a Manual trigger

**As an** Organization Member, **I want to** configure a Manual trigger **so that** authorized users can start the workflow on demand.

**Acceptance Criteria:**

*Happy path*
- [ ] Adding a Manual trigger opens a config panel for defining optional named input variables (name + type + required flag).
- [ ] When triggering via UI, a dialog prompts for the defined input variables before starting.
- [ ] API: `POST /workflows/{id}/executions` with `{ "input": { "var_name": value } }` starts the execution.

*Validation & errors*
- [ ] Triggering via UI without filling required input variables shows inline errors before proceeding.
- [ ] API call with missing required inputs returns HTTP 422 with structured field errors.
- [ ] Users without `workflow:trigger:manual` permission do not see the Run button and get HTTP 403 from the API.

*Edge cases*
- [ ] A workflow with a Manual trigger and no input variables shows a simple "Run" confirmation dialog, not an input form.
- [ ] Triggering the same workflow many times in quick succession creates independent executions (no deduplication for manual triggers).

*Out of scope*
- Triggering with pre-filled input from a page button (Page Builder) — not in MVP for this epic; covered in E07.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: input variable prompt dialog and `POST /workflows/{id}/executions` endpoint pending API + E06.
> Decisions: trigger config (input variable definitions) stored as JSONB in `triggers` column; domain guards against duplicate trigger type per workflow (AddTrigger returns Conflict on second call for same type).

---

## US-063 — Configure a Schedule trigger

**As an** Organization Member, **I want to** schedule a workflow **so that** it runs automatically at defined intervals.

**Acceptance Criteria:**

*Happy path*
- [ ] Cron expression input field with a human-readable preview below it (e.g., "Every Monday at 9:00 AM UTC").
- [ ] Timezone selector (IANA timezone list, searchable) defaults to the organization's configured timezone.
- [ ] "Max concurrent runs" field (default: 1) controls how many executions of this workflow may run at the same time.
- [ ] Schedule is registered with Wolverine on workflow publish; deregistered on archive.

*Validation & errors*
- [ ] Invalid cron expression shows: "Invalid cron expression. Example: `0 9 * * 1` (every Monday at 9 AM)."
- [ ] Cron with a frequency of less than every 5 minutes is blocked: "Minimum schedule interval is 5 minutes."
- [ ] An invalid or missing timezone shows: "Please select a valid timezone."

*Edge cases*
- [ ] If the previous scheduled run is still in progress when the next cron tick fires and `max_concurrent_runs = 1`, the new run is skipped and a warning is logged (not an error).
- [ ] If `max_concurrent_runs > 1`, all concurrent executions proceed independently.
- [ ] Changing the cron expression of an active workflow updates the schedule immediately without archiving and re-publishing.

*Out of scope*
- Date-specific one-time scheduling (e.g., "run once on 2026-12-25") — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: Wolverine cron job registration on publish and deregistration on archive pending E06; cron expression validation (min 5-min interval, IANA timezone) pending API layer.

---

## US-064 — Configure a Webhook trigger

**As an** Organization Member, **I want to** configure a webhook trigger **so that** an external system can start my workflow by sending an HTTP request.

**Acceptance Criteria:**

*Happy path*
- [ ] A unique webhook URL is generated for the workflow on publish (format: `https://api.axis.app/webhooks/{token}`).
- [ ] The URL and an optional HMAC secret are shown in the trigger config panel (secret is masked, with a "copy" button).
- [ ] Payload mapping UI: user maps JSON path expressions from the incoming payload to named workflow input variables.

*Validation & errors*
- [ ] Incoming request without the correct HMAC signature (when secret is configured) returns HTTP 401 immediately, before the workflow is triggered.
- [ ] Incoming request with an invalid JSON body returns HTTP 400: "Request body must be valid JSON."
- [ ] A POST to a webhook URL of an archived workflow returns HTTP 422: "This workflow is archived."

*Edge cases*
- [ ] The webhook URL can be regenerated (old URL is immediately invalidated). A confirmation dialog warns: "Any external system using the current URL will stop working."
- [ ] A payload mapping that references a JSON path not present in the incoming payload sets that input variable to `null` (not an error).
- [ ] Large webhook payloads (> 1 MB) are rejected with HTTP 413: "Payload too large. Maximum size is 1 MB."

*Out of scope*
- GET webhook triggers — POST only in MVP.
- Event-type filtering on a single webhook URL (multiple workflows sharing one URL) — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: unique webhook URL generation, HMAC verification, and payload mapping pending E06 + API layer.

---

## US-065 — Configure an Event trigger

**As an** Organization Member, **I want to** trigger a workflow automatically when a platform event occurs **so that** I don't need to start it manually.

**Acceptance Criteria:**

*Happy path*
- [ ] Event type dropdown lists all available platform events (see epic README for the full list).
- [ ] For `record.*` events: an additional model picker lets the user select which model the event applies to.
- [ ] An optional filter condition (same expression builder as Condition step) lets the user restrict triggering to specific event payloads (e.g., "only trigger if `status == 'approved'`").
- [ ] The event payload is available as workflow input variables matching the event's schema (documented per event type).

*Validation & errors*
- [ ] Selecting a `record.*` event without selecting a model is blocked: "Please select a model for this event type."
- [ ] An invalid filter expression blocks publishing with a clear error.
- [ ] If the model selected for a `record.*` event is deleted, the trigger is flagged as broken and the workflow cannot be triggered until fixed.

*Edge cases*
- [ ] Multiple workflows can listen to the same event type simultaneously; they all trigger independently.
- [ ] An event that triggers a workflow which itself emits another event (e.g., `execution.completed`) does not create an infinite loop — Wolverine enforces a max event chain depth of 10.
- [ ] `execution.completed` event for a workflow does not re-trigger itself (self-triggering is blocked at the platform level).

*Out of scope*
- Custom platform events defined by users — not in MVP.
- Listening to events from external systems (without going through a Webhook trigger) — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ✅ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: Wolverine event subscription wiring and filter expression evaluation pending E06; event type registry and model-picker UI pending API + Frontend.
