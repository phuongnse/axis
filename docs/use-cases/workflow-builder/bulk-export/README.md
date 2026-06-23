# Use case — Bulk export all workflows

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Export all workflows as a ZIP archive so that I have a complete backup.

## Primary actor

- Workspace Admin

## Trigger

- Export all workflows as a zip archive.

## Main flow

1. Actor starts the — Bulk export all workflows flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflow definitions can be exported as portable JSON files and imported into any Axis Workspace, enabling template sharing, backups, and environment migration.

## Acceptance Criteria

*Happy path*
- [ ] "Export all" option on the workflows list triggers a download of a ZIP named `{workspace-slug}-workflows-{date}.zip`.
- [ ] ZIP contains one JSON file per workflow.
- [ ] For workspaces with many workflows (> 20), the ZIP is generated asynchronously and the user receives an in-app notification with a download link when ready (link valid for 24 hours).

*Validation & errors*
- [ ] If the ZIP generation fails, the user receives an error notification and can retry.

*Edge cases*
- [ ] a workspace with 0 workflows: export downloads an empty ZIP with a README.txt explaining the format.

*Out of scope*
- Scheduled automatic backups.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
> - async notification for large exports (> 20 workflows) and workspace-slug prefix in ZIP filename pending API
> - empty-workspace README.txt and file-picker UI pending Frontend.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
