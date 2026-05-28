# Use case — View all models

> **Navigation**: [← Data Modeling](./README.md) · [Use cases index](./README.md#use-cases)

## Purpose

See all models in my organization so that I can understand the data available to me.

## Primary actor

- Organization Member with `data_modeling:model:read`

## Trigger

- User initiates: see all models in my organization

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

## Acceptance Criteria

*Happy path*
- [ ] Models list shows: icon, name, description (truncated), field count, record count, and last modified date.
- [ ] Default sort: alphabetical by name.
- [ ] Search bar filters by name in real time (client-side filter, no API call on each keystroke).

*Validation & errors*
- [ ] If the models list fails to load, an error state with a "Retry" button is shown instead of an empty list.
- [ ] Users without `data_modeling:model:read` who navigate to this URL are redirected to home with a permission error.

*Edge cases*
- [ ] If the org has no models yet, the list shows an empty state with a "Create your first model" CTA.
- [ ] Record count may be slightly behind real-time (cached with 1-minute TTL) to avoid expensive COUNT queries on every list load.

*Deferred capabilities*
- Folders or categories for organizing models.

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
> **Gaps vs spec:** record count column pending denormalized counter or API-layer aggregation.
>
> **Done:** field count is derived from `Fields.Count` at query time.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
