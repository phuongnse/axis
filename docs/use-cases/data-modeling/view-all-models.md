# Use case — View all models

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

see all models in my organization so that I can understand the data available to me.

## Primary actor

- Organization Member with `data_modeling:model:read`

## Trigger

- User initiates: see all models in my organization

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



**Acceptance Criteria:**

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

*Out of scope*
- Folders or categories for organizing models — not in MVP.

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
> - record count column pending denormalized counter or API-layer aggregation
> - field count is derived from Fields.Count at query time.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-models | [source](./wireframes/data-models.excalidraw) | [preview](./wireframes/data-models.svg) |

[← Back to Data Modeling](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
