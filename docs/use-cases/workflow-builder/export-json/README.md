# Use case — Export a workflow as JSON

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Export a workflow as a JSON file so that I can back it up or share it with another team.

## Primary actor

- Organization Member with `workflow:definition:read`

## Trigger

- Export a workflow as a json file.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflow definitions can be exported as portable JSON files and imported into any Axis organization, enabling template sharing, backups, and environment migration.

## Acceptance Criteria

*Happy path*
- [ ] Export option is accessible from the workflow's context menu (list view) and from the workflow editor (toolbar).
- [ ] Exported file is named `{workflow-slug}-{date}.json` and downloaded immediately.
- [ ] The export includes: workflow definition, all step configurations, trigger config, and referenced form/model definitions (structure only, not records).

*Validation & errors*
- [ ] Sensitive values in HTTP step configs (Bearer tokens, Basic auth passwords, API keys) are replaced with `"[REDACTED]"` in the export. A notice is shown: "Sensitive credential values have been removed from the export."

*Edge cases*
- [ ] Exporting a workflow with broken references (e.g., a form that was deleted) still succeeds; the broken reference is exported as-is with a `"broken": true` flag, and a warning is shown to the user.
- [ ] A very large workflow (50+ steps) may take up to 5 seconds to generate; the UI shows a loading state.

*Out of scope*
- Exporting execution history — definitions only.
- Exporting to formats other than JSON (YAML, BPMN) — not in MVP.

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
> - export notice and broken-reference warning UI pending Frontend.
>
> **Decisions:** credential scrubbing in `ExportWorkflowHandler` — keys matching token/api_key/apikey/secret/password/authorization/auth_token/hmac_secret/client_secret/private_key/bearer/access_token/refresh_token replaced with `[REDACTED]` (OrdinalIgnoreCase).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflows | [source](../wireframes/workflows.excalidraw) | [preview](../wireframes/workflows.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
