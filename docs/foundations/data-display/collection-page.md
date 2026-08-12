# Collection Page

> **Navigation**: [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide a consistent enterprise collection workspace in which one primary data table owns page context and record workflows launch into app-scoped managed windows without discarding collection or draft state.

## Consumers

- Product routes that present one structured collection and launch record workflows.

## Activation

- A product route exposes a collection and one or more record workflows, or a valid direct URL supplies a record-window launch intent.

## Guarantees

- Renders one primary table for the collection resource.
- Preserves enabled route-owned query and table state while record workflows open, focus, minimize, save, close, or survive authenticated navigation.
- Uses [docs/foundations/overlays/managed-dialog.md](../overlays/managed-dialog.md) for stable record-workflow identities and lifecycle.
- Keeps record sections within one record workflow rather than creating a second collection workspace.

## Alternate / error flows

- Direct URL: validated dialog mode and record identity act as a launch intent for one managed window after required data loads; the route consumes those parameters with history replacement while preserving unrelated collection search state.
- Existing launch identity: hydration focuses or restores the existing window instead of creating a duplicate or replacing another record's title and content.
- Browser Back or Forward: browser history controls routes and shareable collection state; it does not silently close app-scoped record windows or discard their drafts.
- Browser refresh: the current deep link can launch its one requested window after reload, but the prior session's complete window set and geometry are not restored.
- Unsaved changes: closing a window requests consumer confirmation without discarding input silently; authenticated route navigation alone does not dismiss the draft.
- Validation or concurrency conflict: the affected record window remains open, identifies affected controls, and preserves recoverable input without disturbing sibling windows.
- Small viewport: managed windows use the compact contract while the collection retains one internally scrolling table workspace without document overflow.
- Complex workflow: a dedicated route is allowed only when comparison, long-running work, or multi-record context cannot be represented accessibly in managed windows.

## Acceptance Criteria

- **AC-001** A collection route exposes one primary data table rather than multiple competing collection surfaces.
- **AC-002** Create, view, and edit actions request stable app-scoped managed-window identities; opening an existing identity restores or focuses it without replacing or duplicating other record windows.
- **AC-003** Valid route search parameters provide a shareable launch intent for one record window, are consumed without becoming the live source of window lifecycle, and preserve unrelated collection search state.
- **AC-004** Table search, filter, sort, grouping, column, selection, and paging state remain stable while record windows open, focus, minimize, save, close, or survive authenticated navigation.
- **AC-005** Consumer-defined row and bulk commands compose through the table toolbar/action API without requiring an action column.
- **AC-006** Record windows provide accessible naming, initial focus, active-window keyboard dismissal, focus return, loading, error, validation, disabled, saving, success, and unavailable-record behavior through the managed-window foundation.
- **AC-007** Record-window content owns its scroll region with stable header and action areas and fits supported desktop and compact viewports without document scrolling or horizontal overflow.
- **AC-008** Unsaved changes and optimistic-concurrency conflicts preserve recoverable input and require an explicit user decision before destructive dismissal; route navigation does not bypass that decision.
- **AC-009** Window tabs group sections of the same record only and do not introduce a second primary collection workspace.
- **AC-010** The foundation is product-neutral and does not own feature DTOs, authorization, mutations, localized copy, record validation, or window mechanics.
- **AC-011** A consumer may use a dedicated record route only when its documented workflow requires multi-record comparison, long-running work, or a layout that cannot remain usable in the managed-window contract.
- **AC-012** The shared Resource Workspace owner provides one localized page context, optional status, one primary table, app-owned responsive density, and internal data overflow across registered consumers without absorbing managed-window or feature semantics.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | A collection workspace renders one primary table and composes record and bulk actions without an action column. | AC-001, AC-005, AC-010 | UI component test | Yes |
| AT-002 | UI component | Create, view, and edit actions open stable window identities, deduplicate existing records, preserve table state, and consume a direct-URL launch intent without making URL state own the live workspace. | AC-002, AC-003, AC-004, AC-006 | UI component test | Yes |
| AT-003 | UI component | Loading, unavailable data, validation, unsaved dismissal, concurrency conflict, save, route navigation, and sibling-window changes retain recoverable record state. | AC-004, AC-006, AC-008, AC-009 | UI component test | Yes |
| AT-004 | Browser journey | Direct URL, refresh, Back/Forward, authenticated navigation, multiple record windows, internal scrolling, stable actions, and desktop/compact layout behave without draft loss, overflow, or console errors. | AC-002, AC-003, AC-004, AC-006, AC-007, AC-008, AC-009 | Browser automation | Yes |
| AT-005 | Static frontend | Shared collection and managed-window contracts remain product-neutral, localized consumers typecheck, and dedicated routes remain limited to documented workflow needs. | AC-005, AC-010, AC-011 | Frontend CI | Yes |
| AT-006 | UI component | The complete Resource Workspace owner and five-consumer inventory proves one clean-cutover page/table anatomy, criterion-level standards applicability, responsive internal overflow, 32/44 CSS-pixel control density, localized 320 CSS-pixel reflow, component state coverage, and project-owner review disposition without absorbing managed-window or feature semantics. | AC-001, AC-004, AC-005, AC-007, AC-010, AC-012 | UI component test + Browser automation | Yes |

## Out Of Scope

- Feature-specific forms, API mutations, authorization, validation rules, and localized product copy.
- Persisting the complete managed-window set, geometry, or drafts across reloads or signed-in sessions.
- Multi-record comparison workspaces, dashboards, kanban boards, timelines, spreadsheet editing, or native window pop-outs.
- Persisting collection state to a user profile before an owning preference contract exists.

## Screen flow

| Surface | Required contract |
|---|---|
| Collection workspace | Render one primary data table and consumer-defined toolbar actions inside the authenticated page region. |
| Record link or create action | Request a stable managed-window identity without replacing another open record workflow. |
| Deep-link launch | Validate mode and identity, open or focus one window after required data loads, then consume only the launch parameters. |
| Record window | Use [docs/foundations/overlays/managed-dialog.md](../overlays/managed-dialog.md) for mounting, focus, activation, geometry, minimize, close, and app-session lifetime. |
| Window tabs | Group sections belonging to one record without becoming separate page-level collections. |
| Dismissal and conflict | Preserve input, identify the decision required, and keep sibling windows and collection state unchanged. |

Required UI quality: collection and record-window controls must be keyboard-reachable, visible copy must come from the consuming feature translation layer, browser history must remain predictable, managed windows must not resize the table workspace, and workflows must remain usable at supported desktop and compact widths.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** The shared data table provides one primary collection workspace with internal scrolling and toolbar action composition. Current collection consumers use URL parameters as one-time launch intents, request stable app-scoped window identities, preserve route-owned collection state, retain independent drafts across authenticated navigation, and guard dirty closure without replacing sibling records.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Full workspace persistence, profile-backed collection preferences, native pop-outs, and dedicated multi-record workspaces remain out of scope.
>
> **Verification:** Acceptance proof is tracked in [docs/foundations/data-display/collection-page.evidence.md](./collection-page.evidence.md).
>
> **Decisions:** One route retains one primary collection table. Route search owns shareable collection state and may carry one validated record-window launch intent, but the app-scoped manager owns live window lifecycle, geometry, active state, and drafts. Launch parameters are consumed without clearing unrelated collection state. Browser history controls routes and collection state rather than destructively dismissing record windows. Record-window mechanics are owned once by [docs/foundations/overlays/managed-dialog.md](../overlays/managed-dialog.md); consumers retain product data, forms, mutations, authorization, dirty policy, and copy. Dedicated detail routes require a documented workflow need.
