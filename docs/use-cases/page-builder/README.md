# Page & UI Builder

[← Back to Use Cases](../README.md)

---

## Overview

A drag-and-drop UI builder that allows users to compose pages from pre-built widgets (lists, grids, charts, forms, buttons). Pages are bound to live data models and workflow executions, enabling fully custom internal tools and customer-facing portals.

## Business Value

Without a UI builder, users need a developer to display and interact with their data. With it, business users can build complete applications end-to-end.

## Phase

**Phase 2** — Depends on a stable E03, E04, E05, E06 first.

---

## Use Cases

> Use-case files are not yet authored — E07 is Phase 2 and has not started. The rows below name the planned cuts; create `use-cases/*.md` when the epic begins.

| Use case | Description |
|---|---|---|
| F01 | Page Management | Create, edit, delete, publish/unpublish pages |
| F02 | Widget Library | Pre-built widgets: List, Grid, Form, Chart, Button, Text, Image |
| F03 | Drag & Drop Layout Builder | Compose pages by dragging widgets onto a canvas, resize and arrange |
| F04 | Data Binding | Bind widget data sources to models, records, and workflow outputs |
| F05 | Page Access Control | Control which roles can access each page |

---

## Diagrams

> **Pending** — page-model diagram not yet created. Add `page-model` to `docs/diagrams/generate-diagrams.mjs` when this epic begins.

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

## Acceptance Criteria (Epic Level)

- [ ] Users can create a page and add at least 3 different widget types.
- [ ] A Grid widget bound to a model displays live records with sorting and pagination.
- [ ] A Form widget on a page can be submitted and stores data to the bound model.
- [ ] A Button widget can trigger a manual workflow execution.
- [ ] Pages can be published (accessible without login) or restricted to specific roles.
- [ ] Page layout is responsive and usable on tablet-sized screens.

---

## Open work (agents)

**⏳ Phase 2 — not started.** No `src/Modules/PageBuilder`, no use-case files under `docs/use-cases/page-builder/`. Do not implement until MVP loop (E01–E06) gaps are closed unless explicitly scoped.

---

## Dependencies

- [E01 — Platform Foundation](../platform-foundation/README.md)
- [E02 — Identity & Access Management](../identity-access/README.md)
- [E03 — Data Modeling](../data-modeling/README.md)
- [E05 — Form Builder](../form-builder/README.md)
- [E06 — Workflow Execution Engine](../workflow-engine/README.md)

## Dependents

- None
