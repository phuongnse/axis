# E07 — Page & UI Builder

[← Back to Epics](../README.md)

---

## Overview

A drag-and-drop UI builder that allows users to compose pages from pre-built widgets (lists, grids, charts, forms, buttons). Pages are bound to live data models and workflow executions, enabling fully custom internal tools and customer-facing portals.

## Business Value

Without a UI builder, users need a developer to display and interact with their data. With it, business users can build complete applications end-to-end.

## Phase

**Phase 2** — Depends on a stable E03, E04, E05, E06 first.

---

## Features

| ID | Feature | Description |
|---|---|---|
| [F01](./features/F01-page-management.md) | Page Management | Create, edit, delete, publish/unpublish pages |
| [F02](./features/F02-widget-library.md) | Widget Library | Pre-built widgets: List, Grid, Form, Chart, Button, Text, Image |
| [F03](./features/F03-layout-builder.md) | Drag & Drop Layout Builder | Compose pages by dragging widgets onto a canvas, resize and arrange |
| [F04](./features/F04-data-binding.md) | Data Binding | Bind widget data sources to models, records, and workflow outputs |
| [F05](./features/F05-page-access.md) | Page Access Control | Control which roles can access each page |

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

## Dependencies

- [E01 — Platform Foundation](../E01-platform-foundation/README.md)
- [E02 — Identity & Access Management](../E02-identity-access/README.md)
- [E03 — Data Modeling](../E03-data-modeling/README.md)
- [E05 — Form Builder](../E05-form-builder/README.md)
- [E06 — Workflow Execution Engine](../E06-workflow-engine/README.md)

## Dependents

- None
