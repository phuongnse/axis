# F07 — Workflow Import / Export

[← Back to E04](../README.md)

---

## Description

Workflow definitions can be exported as portable JSON files and imported into any Axis organization, enabling template sharing, backups, and environment migration.

---

## US-072 — Export a workflow as JSON

**As an** Organization Member with `workflow:definition:read`, **I want to** export a workflow as a JSON file **so that** I can back it up or share it with another team.

**Acceptance Criteria:**

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

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: `ExportWorkflowQuery` handler not yet implemented; REDACTED credential scrubbing and file download endpoint pending API layer.

---

## US-073 — Import a workflow from JSON

**As an** Organization Member with `workflow:definition:write`, **I want to** import a workflow from a JSON file **so that** I can quickly set up a workflow that someone else designed.

**Acceptance Criteria:**

*Happy path*
- [ ] "Import workflow" button on the workflows list opens a file picker accepting `.json` files only.
- [ ] After file selection, the system validates the file and shows an import preview: workflow name, step count, and a list of referenced forms/models with their resolution status (exists / will be created / broken).
- [ ] User confirms and the workflow is imported in `Draft` status, ready to edit and publish.

*Validation & errors*
- [ ] Invalid JSON file: "This file is not valid JSON. Please check the file and try again."
- [ ] Unrecognized format (valid JSON but not an Axis workflow export): "This file is not a valid Axis workflow export."
- [ ] Version mismatch (`axis_version` field too new): "This export was created with a newer version of Axis and cannot be imported. Please update Axis first."
- [ ] Workflow name conflict: the user is prompted to rename the workflow before importing.

*Edge cases*
- [ ] A referenced form that doesn't exist in the target org is created automatically during import (using the form definition embedded in the export). If a form with the same name already exists, the user is prompted: "A form named '{name}' already exists. Use existing or create new?"
- [ ] `[REDACTED]` credential values in the import are imported as-is; the user is shown a warning: "N HTTP step(s) have redacted credentials. Configure them after import."
- [ ] Importing a workflow that references a model that doesn't exist in the target org: the model definition is created (fields only, no records). If a model with the same name exists, user is prompted to map to it or create new.
- [ ] Import is transactional: if any part fails mid-import, no partial data is left behind.

*Out of scope*
- Automatic periodic export/backup — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: `ImportWorkflowCommand` handler not yet implemented; import preview, form/model resolution, and transactional import endpoint pending API layer.

---

## US-074 — Bulk export all workflows

**As an** Organization Admin, **I want to** export all workflows as a ZIP archive **so that** I have a complete backup.

**Acceptance Criteria:**

*Happy path*
- [ ] "Export all" option on the workflows list triggers a download of a ZIP named `{org-slug}-workflows-{date}.zip`.
- [ ] ZIP contains one JSON file per workflow.
- [ ] For orgs with many workflows (> 20), the ZIP is generated asynchronously and the user receives an in-app notification with a download link when ready (link valid for 24 hours).

*Validation & errors*
- [ ] If the ZIP generation fails, the user receives an error notification and can retry.

*Edge cases*
- [ ] An org with 0 workflows: export downloads an empty ZIP with a README.txt explaining the format.

*Out of scope*
- Scheduled automatic backups — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: `BulkExportWorkflowsCommand` handler not yet implemented; async ZIP generation and in-app notification for large exports pending E06/notification infrastructure.
