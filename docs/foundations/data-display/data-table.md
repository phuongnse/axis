# Data Table

> **Navigation**: [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide a reusable, typed data-table foundation that lets product features define data semantics once and obtain consistent search, filtering, sorting, grouping, paging, selection, and large-dataset behavior without owning a local table interaction system.

## Primary actor

- Signed-in Axis Platform user working with a structured list or table.

## Trigger

- A product surface renders a collection through a typed data-table definition.

## Main flow

1. The consumer supplies a typed data source, stable row identity, localized messages, and column definitions.
2. The table renders visible columns, derives search, typed filter fields, sorting, grouping, and visibility controls from column capabilities, and composes consumer-defined actions into its toolbar.
3. The user searches, filters, sorts, groups, expands, selects, or changes visible columns without losing table layout stability.
4. The configured data mode applies client processing, numbered paging, or progressive infinite loading consistently.
5. The table renders loading, error, empty, no-result, and pagination states inside its owned region.

## Alternate / error flows

- Client dataset: search, filter, sort, grouping, aggregation, expansion, and numbered paging operate over the complete in-memory dataset.
- Server page: the table emits structured query state and renders backend-owned rows, row count, and page state without applying local operations to a partial dataset.
- Infinite dataset: the table progressively requests cursor pages near the scroll boundary and virtualizes loaded rows when configured.
- Hierarchical dataset: rows expose child rows or an expanded detail renderer without changing the consumer's column contract.
- Hidden column: its generated filter field is removed and any active condition is cleared so an invisible criterion cannot remain active.
- Nested filter: the user combines type-compatible conditions and groups with `AND` or `OR`; invalid or incomplete conditions are identified before a server-owned query is emitted.
- Request failure: current table chrome remains stable and exposes a localized retry action when supplied.
- Unsupported state combination: development builds reject server-owned data modes configured with local filtering, sorting, or grouping.

## Acceptance Criteria

*Definition and rendering*
- **AC-001** A consumer can render a table from one typed definition containing its data source, row identity, localized messages, and semantic column metadata.
- **AC-002** Columns support custom cell and aggregate rendering, visibility, order, sizing, and pinning without feature-local visual overrides.
- **AC-003** The table owns loading, error, empty, no-result, and retry presentation inside a constrained scroll region with a stable header.

*Discovery and query state*
- **AC-004** Full-text search considers visible searchable columns and semantic search values rather than rendered markup.
- **AC-005** A typed filter builder derives fields only from visible filterable columns and supports nested `AND`/`OR` groups with operators and value editors appropriate to text, number, date, date-and-time, boolean, single-choice, and multiple-choice semantics.
- **AC-006** Hiding a column clears every condition that references it, while reset clears global search and the complete filter expression without changing the data definition.
- **AC-007** Sort state supports single or multi-column sorting and exposes accessible state through column header controls.
- **AC-017** Filter state uses a serializable, product-neutral expression contract that preserves typed scalar and list values and can be consumed consistently by client, numbered-page, and infinite data modes.

*Data modes and composition*
- **AC-008** Client mode supports no paging or numbered paging over the complete in-memory dataset.
- **AC-009** Manual page mode supports page number, page size, total row count, and controlled query state without local processing of partial server data.
- **AC-010** Infinite mode supports progressive loading, end/retry/loading states, and optional row virtualization without document-level scrolling.
- **AC-011** Grouping supports aggregate cells and expandable grouped rows; hierarchical rows and custom detail panels use the same controlled expansion model.
- **AC-012** Optional row selection supports page and all-loaded-row semantics and exposes selected rows to consumer-owned bulk actions.
- **AC-016** Consumers can render domain actions in a stable toolbar slot above the column header without adding an action column or coupling the foundation to feature commands.

*Quality*
- **AC-013** Table controls use approved shared UI primitives, localized consumer copy, keyboard interaction, labels, and visible focus states.
- **AC-014** The table fits supported desktop and mobile widths without document-level overflow, aligns body cells to the row start, and keeps header, body, and footer regions visually coherent.
- **AC-015** The foundation API does not depend on TanStack Query, Router, a product API contract, or feature-specific data types.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Typed columns render semantic cells, stable headers, visibility, sorting, consumer-defined toolbar actions, and constrained empty/error/loading states. | AC-001, AC-002, AC-003, AC-007, AC-013, AC-016 | UI component test | Yes |
| AT-002 | UI component | Search and the typed filter builder derive from visible columns, support nested groups and type-specific operators/editors, clear hidden-field conditions, validate incomplete input, and reset correctly. | AC-004, AC-005, AC-006, AC-017 | UI component test | Yes |
| AT-003 | UI component | Client numbered paging and manual page callbacks preserve correct whole-dataset ownership. | AC-008, AC-009, AC-015 | UI component test | Yes |
| AT-004 | UI component | Infinite loading, grouping, expansion, aggregation, selection, and bulk action extension points operate through controlled table state. | AC-010, AC-011, AC-012, AC-015 | UI component test | Yes |
| AT-005 | UI component | Rules catalog consumes the shared definition and exposes dynamic search, filters, sorting, paging, and row actions. | AC-001, AC-004, AC-005, AC-007, AC-008 | UI component test | Yes |
| AT-006 | Browser journey | Rules table keeps its header and toolbar stable, confines scrolling, and fits desktop and mobile without document overflow or console errors. | AC-003, AC-013, AC-014 | Browser automation | Yes |
| AT-007 | Static frontend | Shared and consuming code typechecks, lints, and keeps localized copy valid. | AC-013, AC-015 | Frontend CI | Yes |

## Out Of Scope

- Product-specific API query parameters, authorization, row actions, and bulk operation behavior.
- Translating the product-neutral filter expression into a module-specific database query or search index request.
- Backend full-text indexes, grouping queries, aggregate queries, cursor generation, and export jobs.
- Spreadsheet editing, pivot tables, formulas, charts, and arbitrary cell editors.
- Persisting table preferences to a user profile until an owning preference contract exists.
- Treating a raw object array as a user-facing schema; labels and semantics remain explicit typed metadata.

## Screen flow

| Surface | Required contract |
|---|---|
| Table toolbar | Show configured global search, typed visible-column filter builder, active-filter reset, grouping controls, column controls, and consumer-defined domain actions without layout shift. |
| Header | Show localized labels, accessible sort state, resize affordances when enabled, and pinned columns when configured. |
| Body | Render row-start-aligned semantic cells, grouped or hierarchical rows, selection, and detail content inside the owned scroll viewport. |
| Footer | Show numbered paging, loaded/total progress, or infinite loading state according to the selected data mode. |
| Non-data states | Preserve the table region while showing localized loading, error, empty, or no-result content and an available retry action. |

Required UI quality: controls must be keyboard-reachable, visible copy must come from the consumer's translation layer, table state must not create hidden active criteria, and server-owned modes must never present partial client processing as whole-dataset results.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** A typed TanStack Table and TanStack Virtual based foundation owns client, manual page, and infinite data modes; semantic full-text search; a nested typed filter builder; sorting; numbered paging; grouping; expansion; selection; virtualization; stable table scrolling; consumer-defined toolbar actions; and shadcn-owned controls. The Rules catalog is the first product consumer.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Backend-owned search, grouping, aggregate, cursor, export, and persisted preference contracts remain with future consuming use cases.
>
> **Verification:** `python scripts/axis.py frontend ci`; `python scripts/axis.py frontend script test -- data-table.test.tsx rules-page.test.tsx`; `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts`.
>
> **Decisions:** One typed definition packages data and semantic column metadata; raw data reflection is rejected. Client, manual page, and infinite modes are explicit. Manual modes own whole-dataset processing on the server. Filter fields follow visible columns, use an Axis-owned serializable expression instead of a library-owned wire contract, and clear hidden-field conditions. Header and body share one native table layout and one scroll viewport; virtualization changes row rendering without introducing a second table layout. Advanced capabilities are opt-in so simple consumers do not inherit unused controls.
