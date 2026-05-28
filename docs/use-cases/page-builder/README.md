# Page & UI Builder

[← Back to Use Cases](./README.md)

---

## Overview

A drag-and-drop UI builder that allows users to compose pages from pre-built widgets (lists, grids, charts, forms, buttons). Pages are bound to live data models and workflow executions, enabling fully custom internal tools and customer-facing portals.

## Business Value

Without a UI builder, users need a developer to display and interact with their data. With it, business users can build complete applications end-to-end.

## Phase

**Production** — Same product scope as other domains; implementation follows [PROGRESS.md](../../PROGRESS.md) once the module is started.

---

## Use Cases

| Use case | Summary |
|---|---|



---

## Diagrams

> **Pending** — page-model diagram not yet created. Add `page-model` to `docs/diagrams/generate-diagrams.mjs` when this domain begins.

---

## Widget Types (v1)

| Widget | Description |
|---|---|
| **List** | Display records from a model in a vertical list |
| **Grid / Table** | Tabular display with sortable columns and pagination |
| **Form** | Render a form definition for data entry |
| **Button** | Trigger a workflow or navigate to another page |
| **Text / Heading** | Static text, markdown, or dynamic field value |
| **Chart** | Bar, line, or pie chart bound to aggregated model data |
| **Image** | Static image or dynamic image field value |

---

## Acceptance Criteria (domain)

- [ ] Users can create a page and add at least 3 different widget types.
- [ ] A Grid widget bound to a model displays live records with sorting and pagination.
- [ ] A Form widget on a page can be submitted and stores data to the bound model.
- [ ] A Button widget can trigger a manual workflow execution.
- [ ] Pages can be published (accessible without login) or restricted to specific roles.
- [ ] Page layout is responsive and usable on tablet-sized screens.

---

## Open work (agents)

**⏳ Not started.** No `src/Modules/PageBuilder` yet; add per-use-case specs under `docs/use-cases/page-builder/` before implementation. Prioritize alongside other domain **Open work** — not gated behind a reduced product phase.

---

## Dependencies

- [Platform Foundation](./platform-foundation/README.md)
- [Identity & Access](./identity-access/README.md)
- [Data Modeling](./data-modeling/README.md)
- [Form Builder](./form-builder/README.md)
- [Workflow Engine](./workflow-engine/README.md)

## Dependents

- None
