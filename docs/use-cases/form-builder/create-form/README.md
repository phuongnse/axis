# Use case — Create a form

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Create a new form so that I can design a data collection interface.

## Primary actor

- Organization Member with `form:definition:write`

## Trigger

- User initiates: create a new form

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, edit, and delete form definitions. A form is a reusable collection of fields that can be embedded in workflow Form steps or rendered on a Page Builder page.

## Acceptance Criteria

*Happy path*
- [ ] Creation dialog collects: name (required), description (optional).
- [ ] New form is created immediately with no fields and opens in the form editor.
- [ ] A live preview panel on the right of the editor shows the form as it would appear to a user filling it in.

*Validation & errors*
- [ ] Name: required, 2–200 characters, unique within the org (case-insensitive). Duplicate shows: "A form named '{name}' already exists."

*Edge cases*
- [ ] Creating a form and immediately navigating away without adding fields: the empty form is saved and visible in the forms list.

*Out of scope*
- Form templates / starter library.

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
> **Gaps vs spec:** live preview panel and form editor pending Frontend.
>
> **Decisions:** all form fields stored as JSONB via custom FormFieldConverter using FormFieldType as polymorphic discriminator.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

### form-model

```mermaid
%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%
erDiagram
  FormDefinition ||--o{ FormFieldDefinition : owns
  FormFieldDefinition }o--|| FormFieldType : type
  FormDefinition ||--o{ FormSubmission : runtime
  FormSubmission }o--|| FormSubmissionStatus : status

  FormDefinition {
    uuid id
    string name
    uuid organizationId
  }
  FormFieldDefinition {
    uuid id
    uuid formId
    string key
    FormFieldType type
    jsonb config
  }
  FormSubmission {
    uuid id
    uuid formDefinitionId
    uuid executionId
    jsonb submittedData
  }
```
