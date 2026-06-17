# Use case — Configure an Event trigger

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Trigger a workflow automatically when a platform event occurs so that I don't need to start it manually.

## Primary actor

- Workspace Member

## Trigger

- Trigger a workflow automatically when a platform event occurs.

## Main flow

1. Member selects Event trigger for a workflow and chooses a platform event type.
2. System collects required event-specific configuration, including model selection for record events and optional filter conditions.
3. Workflow stores the trigger configuration and exposes matching event payload variables to downstream steps.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A workflow must have at least one trigger before it can be published. Triggers define how and when a workflow execution starts.

## Acceptance Criteria

*Happy path*
- [ ] Event type dropdown lists all available platform events (see domain README for the full list).
- [ ] For `record.*` events: an additional model picker lets the user select which model the event applies to.
- [ ] An optional filter condition (same expression builder as Condition step) lets the user restrict triggering to specific event payloads (e.g., "only trigger if `status == 'approved'`").
- [ ] The event payload is available as workflow input variables matching the event's schema (documented per event type).

*Validation & errors*
- [ ] Selecting a `record.*` event without selecting a model is blocked: "Please select a model for this event type."
- [ ] An invalid filter expression blocks publishing with a clear error.
- [ ] If the model selected for a `record.*` event is deleted, the trigger is flagged as broken and the workflow cannot be triggered until fixed.

*Edge cases*
- [ ] Multiple workflows can listen to the same event type simultaneously; they all trigger independently.
- [ ] An event that triggers a workflow which itself emits another event (e.g., `execution.completed`) does not create an infinite loop — Wolverine enforces a max event chain depth of 10.
- [ ] `execution.completed` event for a workflow does not re-trigger itself (self-triggering is blocked at the platform level).

*Out of scope*
- Custom platform events defined by users.
- Listening to events from external systems (without going through a Webhook trigger).

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
> - Wolverine event subscription wiring and filter expression evaluation pending workflow-engine
> - event type registry and model-picker UI pending API + Frontend.
>
> **Deferred follow-ups:** Custom platform events defined by users; listening to external-system events without a Webhook trigger.
>
> **Decisions:** Event trigger scope is limited to platform-owned event types until custom event contracts have an approved registry model.
>

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
