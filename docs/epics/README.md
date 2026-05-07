# Epics

[← Back to Docs Home](../README.md)

---

## All Epics

| ID | Epic | Phase | Features |
|---|---|---|---|
| [E01](./E01-platform-foundation/README.md) | Platform Foundation | MVP | Tenant registration, org management, data isolation, subscription plans |
| [E02](./E02-identity-access/README.md) | Identity & Access Management | MVP | Authentication, user management, roles, permissions |
| [E03](./E03-data-modeling/README.md) | Data Modeling | MVP | Custom models, field types, data classes, record CRUD |
| [E04](./E04-workflow-builder/README.md) | Workflow Builder | MVP | Visual canvas, step types, triggers, branching, parallel, import/export |
| [E05](./E05-form-builder/README.md) | Form Builder | MVP | Form definition, field config, workflow integration, submissions |
| [E06](./E06-workflow-engine/README.md) | Workflow Execution Engine | MVP | Execution management, step handlers, error handling, history, retry |
| [E07](./E07-page-builder/README.md) | Page & UI Builder | Phase 2 | Page management, widget library, drag & drop, data binding |

---

## MVP Core Loop

```
[E01] Tenant Setup → [E02] Auth & Users → [E03] Model Data
         → [E04] Build Workflow → [E05] Add Forms
                  → [E06] Execute & Monitor
```

## Phase 2 Expansion

```
[E06] Execution Data → [E07] Build Pages & Widgets → End Users
```
