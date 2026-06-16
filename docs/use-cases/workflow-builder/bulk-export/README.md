# Use case — Bulk export all workflows

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Export all workflows as a ZIP archive so that I have a complete backup.

## Primary actor

- Team account Admin

## Trigger

- Export all workflows as a zip archive.

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
- [ ] "Export all" option on the workflows list triggers a download of a ZIP named `{team account-slug}-workflows-{date}.zip`.
- [ ] ZIP contains one JSON file per workflow.
- [ ] For team accounts with many workflows (> 20), the ZIP is generated asynchronously and the user receives an in-app notification with a download link when ready (link valid for 24 hours).

*Validation & errors*
- [ ] If the ZIP generation fails, the user receives an error notification and can retry.

*Edge cases*
- [ ] A team account with 0 workflows: export downloads an empty ZIP with a README.txt explaining the format.

*Out of scope*
- Scheduled automatic backups.

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
> - async notification for large exports (> 20 workflows) and team account-slug prefix in ZIP filename pending API
> - empty-team account README.txt and file-picker UI pending Frontend.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

