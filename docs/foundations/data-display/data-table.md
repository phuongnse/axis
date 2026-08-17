# Data Table

> **Navigation**: [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide a reusable, typed data-table foundation that lets product features define data semantics once and obtain consistent search, filtering, sorting, grouping, paging, selection, and large-dataset behavior without owning a local table interaction system.

## Consumers

- Product surfaces that render structured collections from typed definitions.

## Activation

- A product surface supplies a typed data-table definition.

## Guarantees

- Accepts a typed data source, stable row identity, locale, localized messages, and semantic column definitions.
- Requires every column to declare one cell kind: `text`, `identifier`, `version`, `revision`, `actor`, `number`, `date`, `dateTime`, `boolean`, `status`, `list`, or `action`.
- Treats lifecycle versions and concurrency revisions as identifiers rather than quantities: they remain start-aligned, use stable `v…` and `r…` display notation where the value is numeric, and sort by their underlying server-owned value rather than their rendered label.
- Formats default-rendered numbers, dates, date-times, booleans, and empty values centrally; missing or invalid values use the canonical compact `N/A` placeholder, numeric columns use end alignment and tabular figures, identifiers use the shared identifier treatment, scalar content stays on one line, and only explicit list cells wrap.
- Provides configured discovery, query-state, data-mode, and toolbar behavior without feature-local table interaction systems.
- Keeps loading, error, empty, no-result, and pagination states inside a stable table region.
- Fits preferred column proportions into the available width on regular viewports, preserves declared column minima whenever the available width can satisfy them, and uses those minima as the compression weight when the complete set cannot fit; compact viewports alone retain table-owned horizontal overflow. Truncated scalar values and headers retain a complete-value hover affordance. Single-line rows vertically center their cells, while a row switches every cell to top alignment only when measured content actually wraps so sibling values remain aligned with the first text line.

## Alternate / error flows

- Client dataset: filter, sort, grouping, aggregation, expansion, and numbered paging operate over the complete in-memory dataset; product free-text search remains server owned.
- Server page: the table emits structured query state and renders backend-owned rows, row count, and page state without applying local operations to a partial dataset.
- Server-page sorting: each enabled column maps to a server-owned total order that is applied before paging; clearing explicit sorting restores the endpoint's default order or search relevance.
- Sortable scalar column: displayed text, number, date/time, or enum values are sortable by default. Enum values use their canonical server representation as the primary order, with deterministic server-owned tie-breakers.
- Semantically unsortable column: a consumer leaves sorting disabled only for action columns, constant values, or composite/list values without an explicitly documented comparison key. Missing backend support for an otherwise sortable scalar is an implementation gap, not a UX exception.
- Infinite dataset: the table progressively requests cursor pages near the scroll boundary and virtualizes loaded rows when configured.
- Hierarchical dataset: rows expose child rows or an expanded detail renderer without changing the consumer's column contract.
- Hidden column: its generated filter field is removed and any active condition is cleared so an invisible criterion cannot remain active.
- Nested filter: the user combines type-compatible conditions and groups with `AND` or `OR`; invalid or incomplete conditions are identified before a server-owned query is emitted.
- Request failure: current table chrome remains stable and exposes a localized retry action when supplied.
- Unsupported state combination: development builds reject server-owned data modes configured with local filtering, sorting, or grouping.

## Acceptance Criteria

*Definition and rendering*
- **AC-001** A consumer can render a table from one typed definition containing its data source, row identity, locale, localized messages, and required semantic column metadata.
- **AC-002** Columns receive a type-correct shared default renderer and support custom cell and aggregate rendering, visibility, order, sizing, and pinning without feature-local alignment or containment overrides.
- **AC-003** The table owns loading, error, empty, no-result, and retry presentation inside a constrained scroll region with a stable header.

*Discovery and query state*
- **AC-004** Full-text input emits controlled server-search intent through [docs/foundations/search/search-experience.md](../search/search-experience.md); the table never matches or ranks rows locally.
- **AC-005** A typed filter builder derives fields only from visible filterable columns and supports nested `AND`/`OR` groups with operators and value editors appropriate to text, number, date, date-and-time, boolean, single-choice, and multiple-choice semantics.
- **AC-006** Hiding a column clears every condition that references it, while reset clears global search and the complete filter expression without changing the data definition.
- **AC-007** Sort state supports single or multi-column sorting and exposes accessible state through column header controls.
- **AC-017** Filter state uses a serializable, product-neutral expression contract that preserves typed scalar and list values and can be consumed consistently by client, numbered-page, and infinite data modes.

*Data modes and composition*
- **AC-008** Client mode supports no paging or numbered paging over the complete in-memory dataset.
- **AC-009** Manual page mode supports page number, page size, total row count, and controlled query state without local search or other processing of partial server data.
- **AC-010** Infinite mode supports progressive loading, end/retry/loading states, and optional row virtualization without document-level scrolling.
- **AC-011** Grouping supports aggregate cells and expandable grouped rows; hierarchical rows and custom detail panels use the same controlled expansion model.
- **AC-012** Optional row selection supports page and all-loaded-row semantics and exposes selected rows to consumer-owned bulk actions.
- **AC-016** Consumers can render domain actions in a stable toolbar slot above the column header without adding an action column or coupling the foundation to feature commands.

*Quality*
- **AC-013** Table controls use localized consumer copy, keyboard interaction, labels, and visible focus states.
- **AC-014** The table proportionally fits columns without horizontal overflow at supported regular widths, confines necessary horizontal overflow to compact widths, vertically centers single-line rows, top-aligns every cell when any content in that row wraps, and keeps single-line siblings aligned to the wrapped content's first line.
- **AC-015** The foundation API does not require a particular client data or navigation library, product API contract, or feature-specific data type.
- **AC-018** Default cell rendering is locale-aware and type-safe: finite quantities are end-aligned with tabular figures; versions, revisions, and identifiers remain start-aligned with stable identifier treatment; date-only values preserve their calendar date; date-times use the declared locale; booleans use localized shared copy; actors expose their display name; empty/invalid values use the canonical `N/A` placeholder; scalar values truncate to one line; and list values alone may wrap. Custom renderers retain the declared kind's alignment and containment.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Typed columns render locale-formatted semantic cells, type-owned horizontal alignment and containment, adaptive single-line/multiline row alignment, viewport-aware column fitting, stable headers, visibility, sorting, consumer-defined toolbar actions, and constrained empty/error/loading states. | AC-001, AC-002, AC-003, AC-007, AC-013, AC-014, AC-016, AC-018 | UI component test | Yes |
| AT-002 | UI component | Controlled search emits server intent without matching rows locally, while the typed filter builder derives from visible columns, supports nested groups and type-specific operators/editors, clears hidden-field conditions, validates incomplete input, and resets correctly. | AC-004, AC-005, AC-006, AC-017 | UI component test | Yes |
| AT-003 | UI component | Client numbered paging and manual page callbacks preserve correct whole-dataset ownership. | AC-008, AC-009, AC-015 | UI component test | Yes |
| AT-004 | UI component | Infinite loading, grouping, expansion, aggregation, selection, and bulk action extension points operate through controlled table state. | AC-010, AC-011, AC-012, AC-015 | UI component test | Yes |
| AT-005 | UI component | Rules catalog consumes the shared definition and exposes URL-backed server search, whole-dataset sorting, paging, and row actions without partial client processing. | AC-001, AC-004, AC-007, AC-009 | UI component test | Yes |
| AT-006 | Browser journey | Rules table keeps its header and toolbar stable, confines scrolling, and fits desktop and mobile without document overflow or console errors. | AC-003, AC-013, AC-014 | Browser automation | Yes |
| AT-007 | Static frontend | Shared and consuming code typechecks, lints, and keeps localized copy valid. | AC-013, AC-015 | Frontend CI | Yes |

## Out Of Scope

- Product-specific API query parameters, authorization, row actions, and bulk operation behavior.
- Translating the product-neutral filter expression into a module-specific database query or search index request.
- Backend full-text indexes, grouping queries, aggregate queries, cursor generation, and export jobs; search provider behavior is owned by [docs/playbooks/search.md](../../playbooks/search.md).
- Spreadsheet editing, pivot tables, formulas, charts, and arbitrary cell editors.
- Persisting table preferences to a user profile until an owning preference contract exists.
- Treating a raw object array as a user-facing schema; labels and semantics remain explicit typed metadata.

## Screen flow

| Surface | Required contract |
|---|---|
| Table toolbar | Show configured global search, typed visible-column filter builder, active-filter reset, grouping controls, column controls, and consumer-defined domain actions without layout shift. |
| Header | Show localized labels, accessible sort state, type-owned alignment, resize affordances when enabled, and pinned columns when configured. |
| Body | Render semantic cells with centralized locale formatting and containment; vertically center a row while all cells remain one line, then top-align the whole row to the first content line when any typed value wraps. Keep grouped or hierarchical rows, selection, and detail content inside the owned scroll viewport. |
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
> **Implemented:** The typed data-table foundation owns client, manual-page, and infinite data modes; required semantic cell kinds and locale formatting; type-owned horizontal alignment and containment; measured row-level vertical alignment; regular-width column fitting with compact-only horizontal overflow; a nested typed filter builder; sorting; numbered paging; grouping; expansion; selection; virtualization; stable table scrolling; and consumer-defined toolbar actions.
>
> **Gaps vs spec:** None. Generic client mode still supports structured filtering over a complete in-memory dataset, but free-text matching is always emitted as controlled server intent.
>
> **Deferred follow-ups:** Backend-owned grouping, aggregate, cursor, export, and persisted preference contracts remain with future consuming use cases.
>
> **Verification:** Acceptance proof is tracked in [docs/foundations/data-display/data-table.evidence.md](./data-table.evidence.md).
>
> **Decisions:** One typed definition packages data, locale, and required semantic column metadata; raw data reflection is rejected. The shared renderer owns scalar formatting, the canonical `N/A` placeholder for missing or invalid values, horizontal alignment, measured row-level vertical alignment, viewport-aware sizing, and containment, while product meanings such as translated enum/status labels remain consumer-owned custom rendering. `number` is reserved for quantities and measures. `version` and `revision` are identifiers even when persisted as integers; numeric versions render with `v` notation and revisions with `r` notation without localized grouping or end alignment. Actor cells render and client-sort by the server-owned display name and keep stable actor identity out of the primary visual label. Scalar cells are one line by default; only an explicit `list` kind wraps, and truncated scalar/header content exposes its complete formatted value. A row remains vertically centered while every cell is one line; if any cell actually wraps, the entire row top-aligns and one-line siblings share its first-line edge. Preferred column sizes act as proportions at regular widths; the fitter clamps feasible columns to their declared minima and otherwise compresses in proportion to those minima, while compact widths preserve preferred sizes and own horizontal scrolling. Client, manual-page, and infinite modes are explicit. Manual modes keep whole-dataset processing on the server, including every enabled sort before paging; clearing sort returns to the server default or relevance order. Displayed scalar text, number, version, revision, actor, date/time, and enum columns are sortable by default; enums use canonical server values plus deterministic tie-breakers. Sorting stays disabled only for actions, constants, and composite/list values without a documented comparison key. Missing backend support is an implementation gap rather than a consumer-specific UX exception. Filter fields follow visible columns, use a product-neutral serializable expression, and clear hidden-field conditions. Advanced capabilities are opt-in so simple consumers do not inherit unused controls.
