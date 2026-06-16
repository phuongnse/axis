# Use case — Configure a Webhook trigger

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure a webhook trigger so that an external system can start my workflow by sending an HTTP request.

## Primary actor

- Team account Member

## Trigger

- Configure a webhook trigger.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A workflow must have at least one trigger before it can be published. Triggers define how and when a workflow execution starts.

## Acceptance Criteria

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
- GET webhook triggers — POST only.
- Event-type filtering on a single webhook URL (multiple workflows sharing one URL).

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
> **Gaps vs spec:** unique webhook URL generation, HMAC verification, and payload mapping pending workflow-engine + API layer.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

