# Use case — Open and submit an assigned form

> **Navigation**: [← Form Builder](./README.md)

## Purpose

Open the form link and submit my responses so that the workflow can continue.

## Primary actor

- assignee

## Trigger

- User initiates: open the form link and submit my responses

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow reaches a Form step, the engine creates a Form Task and notifies the assignee. The assignee opens a unique link, fills the form, and submits it. The engine then validates and continues the workflow.

## Acceptance Criteria

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
> **Gaps vs spec:** `SubmitFormByTokenCommand` + anonymous `POST /api/form-tasks/{token}/submit` ✅. Standalone form page, field validation UX, pre-signed file upload, and multi-tab deduplication pending Frontend.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-submission | [source](./wireframes/form-submission.excalidraw) | [preview](./wireframes/form-submission.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
