# Use case — Export a workflow as JSON

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Export a workflow as a JSON file so that I can back it up or share it with another team.

## Primary actor

- Workspace Member with `workflow:definition:read`

## Trigger

- Export a workflow as a json file.

## Main flow

1. Actor opens the workflow list context menu or workflow editor toolbar and selects **Export JSON**.
2. System verifies the actor has `workflow:definition:read` for the workflow's Workspace.
3. System loads the workflow definition, step configurations, and trigger configuration.
4. System removes sensitive credential values from exported step configuration fields.
5. System serializes the export using the public API JSON contract casing (`camelCase`).
6. System downloads `{workflow-slug}-{date}.json`.
7. Actor can store the file as a backup or share it for import into another Axis Workspace.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflow definitions can be exported as portable JSON files and imported into any Axis Workspace, enabling template sharing, backups, and environment migration.

## Acceptance Criteria

*Happy path*
- [ ] Export option is accessible from the workflow's context menu (list view) and from the workflow editor (toolbar).
- [ ] Exported file is named `{workflow-slug}-{date}.json` and downloaded immediately.
- [ ] The export includes: workflow definition, all step configurations, and trigger config.
- [ ] Exported JSON uses the public API contract casing (`camelCase`) so generated frontend types, OpenAPI docs, and downloaded workflow files stay consistent.

*Validation & errors*
- [ ] Sensitive values in HTTP step configs (Bearer tokens, Basic auth passwords, API keys) are replaced with `"[REDACTED]"` in the export. A notice is shown: "Sensitive credential values have been removed from the export."

*Edge cases*
- [ ] Exporting a workflow with broken references (e.g., a form that was deleted) still succeeds; the broken reference is exported as-is with a `"broken": true` flag, and a warning is shown to the user.
- [ ] A very large workflow (50+ steps) may take up to 5 seconds to generate; the UI shows a loading state.

*Out of scope*
- Exporting execution history — definitions only.
- Exporting to formats other than JSON (YAML, BPMN).

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
> - broken-reference `"broken": true` flag pending data-modeling/form-builder integration
> - referenced form/model definition export pending an owning contract across Workflow Builder, Form Builder, and Data Modeling.
> - export notice and broken-reference warning UI pending Frontend.
>
> **Deferred follow-ups:** Exporting execution history (definitions only today); export formats other than JSON (YAML, BPMN).
>
> **Decisions:** credential scrubbing in `ExportWorkflowHandler` — keys matching token/api_key/apikey/secret/password/authorization/auth_token/hmac_secret/client_secret/private_key/bearer/access_token/refresh_token replaced with `[REDACTED]` (OrdinalIgnoreCase).
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
