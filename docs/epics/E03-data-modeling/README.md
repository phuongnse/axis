# E03 — Data Modeling

[← Back to Epics](../README.md)

---

## Overview

Allow users to define their own data structures — Models (like database tables) and Data Classes (reusable nested types). Users can then create, read, update, and delete records against those models. Schemas are defined at runtime and stored as metadata, not as actual DB schema changes.

## Business Value

Custom data modeling is the core differentiator of Axis. Without it, the platform is just another workflow tool with a fixed data structure. With it, users can model any business domain.

## Phase

**MVP**

---

## Features

| ID | Feature | Description |
|---|---|---|
| [F01](./features/F01-model-definition.md) | Model Definition | Create, edit, delete custom models within an org |
| [F02](./features/F02-field-types.md) | Field Type System | Text, Number, Date, Boolean, Enum, Relation, File, JSON |
| [F03](./features/F03-data-classes.md) | Data Class Management | Reusable nested object types used as field types |
| [F04](./features/F04-data-records.md) | Data Record CRUD | Create, read, update, delete records against any model |

---

## Diagrams

![Data Model Structure](./diagrams/data-model.png)

---

## Core Concepts

### Model
A user-defined entity type, equivalent to a database table conceptually. Example: `Order`, `Customer`, `Invoice`.

### Field
A typed attribute on a Model. Each field has a name, type, validation rules, and optionally a display label.

### Data Class
A reusable, structured type composed of multiple fields. Used as a field type within a Model (similar to an embedded object). Example: `Address`, `ContactInfo`.

### Record
A concrete instance of a Model. Records are stored in the tenant's schema using a flexible JSONB-backed storage strategy.

---

## Supported Field Types

| Type | Description |
|---|---|
| `Text` | Short or long text string |
| `Number` | Integer or decimal |
| `Boolean` | True / False |
| `Date` | Date or DateTime |
| `Enum` | One value from a predefined list |
| `Relation` | Reference to a record of another Model |
| `DataClass` | Embedded nested object (references a Data Class) |
| `File` | File attachment reference |
| `JSON` | Raw JSON blob |

---

## Acceptance Criteria (Epic Level)

- [ ] Users can create a model with at least 5 different field types.
- [ ] Relation fields correctly link records across models.
- [ ] Data classes can be nested inside models and reused across multiple models.
- [ ] Deleting a field displays a warning about data loss and requires confirmation.
- [ ] Records can be filtered, sorted, and paginated via API.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `DataModel`, `Field`, `DataRecord` aggregates; all field types and domain events |
| Application | ✅ Done | All command/query handlers; repository interfaces |
| Infrastructure | ⚠️ Partial | EF Core mappings, repositories, JSONB field converters, pagination+search (`GetPagedAsync`). Missing: per-field filter/sort queries (US-043); bulk delete/export (US-046) |
| API | ⚠️ Partial | Minimal API — `/api/models`, `/api/data-classes`, `/api/models/{id}/records`. Missing: HTTP 422 structured field errors on record endpoints (US-035); per-field filter conditions and sort-by-column (US-043); bulk delete and CSV export (US-046) |
| Frontend | ⏳ Pending | — |

---

## Dependencies

- [E01 — Platform Foundation](../E01-platform-foundation/README.md)
- [E02 — Identity & Access Management](../E02-identity-access/README.md)

## Dependents

- [E04 — Workflow Builder](../E04-workflow-builder/README.md)
- [E05 — Form Builder](../E05-form-builder/README.md)
- [E07 — Page Builder](../E07-page-builder/README.md)
