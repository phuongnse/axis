# Collection Page

> **Navigation**: [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide a consistent enterprise collection workspace in which one primary data table owns page context and record create, view, and edit workflows open in a route-backed dialog without discarding collection state.

## Primary actor

- Signed-in Axis Platform user managing records in a structured collection.

## Trigger

- A product route exposes a collection and one or more record workflows.

## Main flow

1. The route renders one primary data table for its resource.
2. Search, filter, sort, grouping, visible columns, selection, and paging remain available in the table workspace.
3. The user creates a record or opens an existing row.
4. The route records dialog mode and selected record identity in URL search state and opens the record dialog.
5. The user views or edits record content, using dialog tabs only when the record has multiple coherent sections.
6. On save or close, the table keeps its prior query state, the dialog returns focus to the invoking control or row, and refreshed data is reconciled without a page reset.

## Alternate / error flows

- Direct URL or browser refresh: the route restores the table state and opens the requested record dialog after required data loads.
- Browser Back: closes or restores the route-backed dialog before leaving the collection route.
- Unsaved changes: closing or navigating requests confirmation without discarding input silently.
- Validation or concurrency conflict: the dialog remains open, identifies affected controls, and preserves recoverable user input.
- Small viewport: the same dialog contract uses the available viewport while retaining an internal scroll region and stable actions.
- Complex workflow: a dedicated route is allowed only when comparison, long-running work, or multi-record context cannot be represented accessibly in a record dialog.

## Acceptance Criteria

*Workspace contract*
- **AC-001** A collection route exposes one primary data table rather than multiple competing collection surfaces.
- **AC-002** Create, view, and edit workflows open through one route-backed record-dialog contract whose mode and selected record identity are shareable and restorable.
- **AC-003** Table search, filter, sort, grouping, column, selection, and paging state remain stable while a record dialog opens, saves, closes, refreshes, or follows browser history.
- **AC-004** Consumer-defined row and bulk commands compose through the table toolbar/action API without requiring an action column.

*Dialog quality*
- **AC-005** Record dialogs provide accessible naming, initial focus, keyboard dismissal rules, focus return, loading, error, validation, disabled, saving, and success behavior.
- **AC-006** Dialog content owns its scroll region with stable header and action areas and fits supported desktop and mobile viewports without document scrolling or horizontal overflow.
- **AC-007** Unsaved changes and optimistic-concurrency conflicts preserve recoverable input and require an explicit user decision before destructive dismissal.
- **AC-008** Dialog tabs group sections of the same record only and do not introduce a second primary collection workspace.

*Foundation boundary*
- **AC-009** The foundation is product-neutral and does not own feature DTOs, authorization, mutations, copy, or record validation rules.
- **AC-010** A consumer may use a dedicated record route only when its documented workflow requires multi-record comparison, long-running work, or a layout that cannot remain usable in the responsive dialog contract.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | A collection workspace renders one primary table and composes record and bulk actions without an action column. | AC-001, AC-004, AC-009 | UI component test | Yes |
| AT-002 | UI component | Create, view, and edit dialog state round-trips through URL search state and preserves table query state and focus. | AC-002, AC-003, AC-005 | UI component test | Yes |
| AT-003 | UI component | Loading, validation, unsaved dismissal, concurrency conflict, save, and error states remain recoverable inside the dialog. | AC-005, AC-007 | UI component test | Yes |
| AT-004 | Browser journey | The collection and responsive dialog keep internal scrolling, stable actions, browser history, and desktop/mobile layout without overflow or console errors. | AC-002, AC-003, AC-006, AC-008 | Browser automation | Yes |
| AT-005 | Static frontend | Shared contracts remain product-neutral, localized consumers typecheck, and no action-column dependency is introduced. | AC-004, AC-009, AC-010 | Frontend CI | Yes |

## Out Of Scope

- Feature-specific forms, API mutations, authorization, validation rules, and localized product copy.
- Multi-record comparison workspaces, dashboards, kanban boards, timelines, or spreadsheet editing.
- Persisting collection state to a user profile before an owning preference contract exists.

## Screen Flow

| Surface | Required contract |
|---|---|
| Collection workspace | Render one primary data table and consumer-defined toolbar actions inside the authenticated page region. |
| Record dialog | Restore mode and record identity from URL state, keep header/actions stable, and confine content scrolling. |
| Dialog tabs | Group sections belonging to the selected record without becoming separate page-level collections. |
| Dismissal and conflict | Preserve input, identify the decision required, and return focus to the originating table control when resolved. |

Required UI quality: collection and dialog controls must be keyboard-reachable, visible copy must come from the consuming feature translation layer, browser history must remain predictable, dialog content must not resize the table workspace, and record workflows must remain usable at supported mobile and desktop widths.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** Shared DataTable and responsive Dialog contracts provide the collection workspace, stable internal scrolling, toolbar action composition, and record-dialog layout. Business Objects and Rules use URL search state for create, view, and edit dialogs, preserve table state behind the dialog, and protect unsaved editable records before dismissal.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Acceptance proof is tracked in the sibling evidence sidecar.
>
> **Decisions:** One route has one primary collection table. Record workflows use URL-backed responsive dialogs by default. Dialog tabs organize one record; they do not create competing collection surfaces. Dedicated detail routes require a documented workflow need rather than feature preference.
