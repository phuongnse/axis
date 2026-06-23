# Use case — Configure an HTTP Request step

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure an HTTP Request step so that my workflow can integrate with external services.

## Primary actor

- Workspace Member

## Trigger

- Configure an http request step.

## Main flow

1. Actor starts the — Configure an HTTP Request step flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each step has a type that determines what it does when executed. Users configure each step type through the canvas side panel.

## Acceptance Criteria

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
- GraphQL or gRPC step types.
- Response streaming.

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
> - HTTP execution and Test request button pending workflow-engine + Frontend
> - credential storage redaction is enforced at export (keys matching token/api_key/secret/password/authorization/etc. replaced with `[REDACTED]` in `ExportWorkflowHandler`).
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

