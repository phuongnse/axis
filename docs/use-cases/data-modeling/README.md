# Data Modeling

[← Back to Use Cases](../README.md)

---

## Overview

Allow users to define their own data structures — Models (like database tables) and Data Classes (reusable nested types). Users can then create, read, update, and delete records against those models. Schemas are defined at runtime and stored as metadata, not as actual DB schema changes.

## Business Value

Custom data modeling is the core differentiator of Axis. Without it, the platform is just another workflow tool with a fixed data structure. With it, users can model any business domain.

## Phase

**MVP**

---

## Use Cases

### Models

| Use case | Summary |
|---|---|
| [Add a field to a model](add-field/) | Add a field of any supported type to a model so that I can capture the data I need. |
| [Create a model](create-model/) | Create a new model so that I can start defining the data structure for my business objects. |
| [Delete a model](delete-model/) | Delete a model so that I can clean up unused data structures. |
| [Edit a model](edit-model/) | Edit an existing model so that I can add, remove, or rename fields as requirements evolve. |
| [Configure field validation rules](field-validation/) | Configure validation rules on a field so that data quality is enforced at input time. |
| [View all models](list-models/) | See all models in my organization so that I can understand the data available to me. |
| [Reorder fields](reorder-fields/) | Reorder fields in a model so that the display order matches our team's mental model. |

### Data classes

| Use case | Summary |
|---|---|
| [Create a data class](create-data-class/) | Create a data class so that I can define a reusable nested object structure. |
| [Use a data class as a field in a model](data-class-field/) | Use a data class as a field type in a model so that I can embed structured nested objects without du |
| [Delete a data class](delete-data-class/) | Delete a data class |
| [Edit a data class](edit-data-class/) | Edit a data class so that I can add or remove fields as requirements change. |

### Records

| Use case | Summary |
|---|---|
| [Bulk operations on records](bulk-records/) | Select multiple records and perform bulk actions so that I can manage large datasets efficiently. |
| [Create a record](create-record/) | Create a new record for a model so that I can store business data. |
| [Delete a record](delete-record/) | Delete a record so that I can remove outdated or incorrect entries. |
| [Edit a record](edit-record/) | Edit an existing record so that I can update out-of-date information. |
| [View records list](list-records/) | See all records for a model so that I can browse and find the data I need. |
| [Filter and search records](search-records/) | Filter and search records so that I can find the specific data I need quickly. |



---

## Diagrams

![Data Model Structure](./create-model/data-model.svg)

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

## Acceptance Criteria (domain)

- [ ] Users can create a model with at least 5 different field types.
- [ ] Relation fields correctly link records across models.
- [ ] Data classes can be nested inside models and reused across multiple models.
- [ ] Deleting a field displays a warning about data loss and requires confirmation.
- [ ] Records can be filtered, sorted, and paginated via API.

---

## Code style

Repo-wide C# conventions (explicit types, naming, Allman braces) are enforced via [`.editorconfig`](../../../.editorconfig). Run `dotnet format Axis.sln` before push ([CONTRIBUTING.md](../../../CONTRIBUTING.md)).

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `DataModel`, `Field`, `DataRecord` aggregates; all field types and domain events |
| Application | ✅ Done | All command/query handlers; `RecordFieldValidator`; `BulkDeleteRecordsHandler`; `ExportRecordsCsvHandler` |
| Infrastructure | ✅ Done | EF Core mappings, repositories, JSONB field converters; `GetPagedAsync` with filter/sort; `BulkDeleteAsync`; `GetAllForExportAsync`. Database `axis_datamodeling` with initial migration `InitialCreate` ([ADR-011](../../TECH_STACK.md#adr-011-per-module-database-with-schema-per-tenant-inside), [ADR-023](../../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)). DbContext + UnitOfWork inlined per ADR-017. `OrganizationVerifiedHandler` provisions tenant schema via `TenantModuleProvisionAttempt` (reports `TenantModuleProvisionReportEvent` to Identity; retries via `RetryTenantModuleProvisionHandler` + shared `TenantSchemaProvisioner`, tenant provisioning use case). `Axis.DataModeling.Contracts` + `DataModelingEventMapper` publish 9 Avro lifecycle/field events via Wolverine outbox → Kafka ([ADR-019](../../TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope), [ADR-025](../../TECH_STACK.md#adr-025-transport-selection-rule-by-message-name-suffix)) (PR #101). **Done:** `ModelDeletedHandler` in FormBuilder + WorkflowBuilder. **Deferred (PR #101 follow-up):** Kafka consumer in WorkflowBuilder for field delete refs. |
| API | ✅ Done | 7 record endpoints (CRUD + bulk-delete + CSV export); filter/sort params; HTTP 422 `ValidationProblemDetails` on create/update |
| Frontend | ⏳ Pending | — |

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend** | ⚠️ polish | HTTP 422/409 on records ([data-records](./README.md)); relation display-field resolution; model plan limits **not in subscription-plans** (spec mentions 402 — product decision). 30-day purge jobs deferred. |
| **Frontend** | ⏳ | Model/record UI, filters, data-class sub-forms — all US callouts mark Frontend ⏳. |

Module API is largely ✅; grep `API: ⏳` in linked use-case files only when adding endpoints.

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)
- [Identity & Access](../identity-access/README.md)

## Dependents

- [Workflow Builder](../workflow-builder/README.md)
- [Form Builder](../form-builder/README.md)
- [Page Builder](../page-builder/README.md)
