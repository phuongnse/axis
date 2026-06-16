# Use case — Import a workflow from JSON

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Import a workflow from a JSON file so that I can quickly set up a workflow that someone else designed.

## Primary actor

- Team account Member with `workflow:definition:write`

## Trigger

- Import a workflow from a json file.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflow definitions can be exported as portable JSON files and imported into any Axis team account, enabling template sharing, backups, and environment migration.

## Acceptance Criteria

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
- [ ] A referenced form that doesn't exist in the target team account is created automatically during import (using the form definition embedded in the export). If a form with the same name already exists, the user is prompted: "A form named '{name}' already exists. Use existing or create new?"
- [ ] `[REDACTED]` credential values in the import are imported as-is; the user is shown a warning: "N HTTP step(s) have redacted credentials. Configure them after import."
- [ ] Importing a workflow that references a model that doesn't exist in the target team account: the model definition is created (fields only, no records). If a model with the same name exists, user is prompted to map to it or create new.
- [ ] Import is transactional: if any part fails mid-import, no partial data is left behind.

*Out of scope*
- Automatic periodic export/backup.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ⚠️ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:**
> - import preview dialog, form/model resolution (auto-create or prompt), and file-picker UI pending Frontend
> - handler skips invalid transitions/triggers rather than stopping — full transactional rollback not yet implemented in the API layer.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

