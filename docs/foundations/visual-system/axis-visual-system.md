# Axis Visual System

> **Navigation**: [docs/foundations/visual-system/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Define one calm, precise, data-first visual language for the Axis product so every feature composes the same hierarchy, density, interaction states, page archetypes, and managed-workspace behavior instead of inventing a page-local style.

## Consumers

- The authenticated app shell, shared frontend patterns, and every product feature surface.
- Public entry, authentication, onboarding, and informational surfaces that use the same tokens and interaction states with a different page anatomy.
- Dedicated workbenches whose workflow cannot remain usable inside a resource workspace or managed window.

## Activation

- Any frontend design, implementation, review, migration, or new component decision that affects visible hierarchy, layout, density, typography, color, elevation, motion, responsive behavior, or page anatomy.

## Guarantees

- Presents Axis as a calm enterprise workspace: neutral surfaces structure information, brand color marks primary intent and focus, and semantic colors communicate status.
- Uses one explicit source hierarchy: `theme/axis-theme.json` owns semantic color, font, and radius values; upstream primitives own their reviewed baseline; shared app patterns own Axis compositions; feature code owns layout and product behavior only.
- Composes the [App Frame](../app-shell/app-frame.md), [Collection Page](../data-display/collection-page.md), [Data Table](../data-display/data-table.md), and [Managed Dialog](../overlays/managed-dialog.md) foundations without redefining their mechanics or evidence.
- Provides a Resource Workspace as the default management-page archetype: one primary data table retains collection context while create, view, and edit workflows open in app-scoped managed windows.
- Provides a Dedicated Workbench only for workflows whose canvas, comparison, long-running process, or dependent panels require a route-sized work area.
- Keeps light and dark modes, desktop and compact layouts, pointer and keyboard interaction, and all product states within the same visual hierarchy.
- Owns async feedback once for the whole product: shared timing, pending indicators, action buttons, DataTable loading, accessibility state, and source-policy enforcement replace feature-local spinners, skeletons, timers, and loading-label morphs.
- Requires one accepted golden reference before cross-feature migration and uses that frozen reference as the visual comparison target for later consumers.

Async state uses four product-wide patterns:

| Work type | Immediate state | Delayed visual state | Geometry and replacement |
|---|---|---|---|
| Initial content | Owning region is `aria-busy`; unresolved actions are unavailable. | Shared indicator or skeleton appears after 300 milliseconds and, once visible, remains stable for at least 400 milliseconds. | The unresolved region reserves its normal footprint; ready content replaces it without first rendering an empty state. |
| Background refresh | Existing content remains interactive unless the exact action requires a lock; the region may expose `aria-busy`. | No initial-load indicator or skeleton replaces current content. | Current rows, panels, and scroll positions remain mounted until fresh authoritative data replaces them. |
| User action | The exact action locks and exposes `aria-busy` immediately. | `AsyncButton` replaces the leading icon with the shared spinner after 300 milliseconds while keeping the visible label and width stable. | Success, failure, or recovery owns the subsequent state; feature code does not insert a temporary status row beside the action. |
| Context transition | Existing workspace content and competing actions become inert immediately. The content remains visually stable for a fast authoritative refresh, then an opaque neutral surface obscures it if delayed; the context-owning account view remains open. | Neutral progress may appear after 500 milliseconds and, once visible, remains for at least 600 milliseconds. | The authenticated frame, route geometry, and account view remain mounted; no informational notice, temporary panel, or document scrollbar is introduced. |

Shared async patterns own these timing values internally; feature and route code supplies only semantic state and cannot import the timing hook or provide local milliseconds. `PendingIndicator`, `AsyncButton`, selectable-option content, and DataTable are the only owners of pending visuals. Feature and route code may compose these owners but may not import spinner or skeleton primitives, add pending animation utilities, or present `isFetching` as an initial DataTable load.

Page anatomy is owned at the same level. `PageLayout` accepts an approved semantic archetype and owns authenticated route scrolling and responsive insets; feature code does not select an implementation scroll mode. `PageHeader` and `SectionHeader` own page and section hierarchy; `EntryLayout` owns the full-viewport public-entry frame, utilities slot, horizontal-overflow boundary, and centered content region. Resource Workspaces, Overviews, and Dedicated Workbenches each receive their owned geometry, while entry or informational features supply content without rebuilding the surrounding viewport anatomy.

The UI dependency direction is fixed:

| Layer | Owns | Must not own |
|---|---|---|
| Semantic theme | Product color, typography, radius, and focus values | Components or feature behavior |
| Upstream primitives | Accessible control mechanics and reviewed registry visuals | Product variants, business state, or feature imports |
| Semantic interaction | Actions, choices, forms, feedback, async timing, and focus recovery | Page-specific layout or business meaning |
| Page archetypes | Route hierarchy, responsive geometry, and scroll ownership | Product-specific data or lifecycle |
| App shell | Stable viewport, navigation, overlays, and one context-transition boundary | Feature refresh choreography or page-local loading notices |
| Feature composition | Product state, data, copy, and authorized actions | Global timing, control visuals, scroll containers, or alternate page anatomies |

Management surfaces default to Resource Workspace; dashboard and home surfaces use Overview; only an approved canvas, workflow builder, comparison, or dependent-panel experience uses Dedicated Workbench. Popovers own short contextual choices, managed windows own long-lived tasks and drafts, and alert dialogs own bounded confirmation.

Visual roles are measurable and owned once:

| Dimension | Required role | Owner / value |
|---|---|---|
| Semantic color and fonts | Background/foreground pairs, focus, status, heading, and body families | Exact values come only from `theme/axis-theme.json` and its generated projection. Contrast is at least WCAG AA: 4.5:1 for normal text and 3:1 for large text, UI boundaries, and meaningful non-text state. |
| Radius | Flat; control; compact floating; task overlay | Collection tables use `rounded-sm`; controls use the reviewed primitive default with an 8-pixel theme base; popovers, menus, and select lists use `rounded-lg`; alert dialogs, managed windows, docks, and switchers use `rounded-xl`. Features do not choose another radius tier. |
| Elevation | Base; compact floating; managed | Routes, cards, forms, and tables use border or ring with `shadow-none`; popovers, menus, and select lists use `shadow-md` plus their reviewed ring; bounded dialogs and managed windows use at most `shadow-lg` plus a one-pixel `foreground/10` ring; docks and switchers use at most `shadow-xl` plus the same ring. |
| Icons | Control; navigation; empty state | Lucide is the only product icon family. Control icons are 16 pixels, navigation icons are 20 pixels, and empty-state icons are 24 pixels. Icon-only controls have an accessible name and tooltip where the action is not otherwise visible. |
| Motion | State; compact floating; direct manipulation | Color, border, and opacity state changes use 150ms ease-out; compact floating surfaces enter and exit in 100ms; drag and resize track the pointer without easing. Reduced motion removes slide, zoom, and spatial transition while retaining immediate state, focus, and at most 100ms opacity feedback. |

## Alternate / error flows

- Loading: mark the owning control or region busy immediately, but delay visual pending feedback for 300 milliseconds so fast work completes without a flash. Once shown, keep the indicator visible for at least 400 milliseconds unless newer content, an error, or a recovery state replaces it. Pending feedback occupies a reserved, overlaid, or fixed-size icon slot and never inserts transient layout that changes menu, popover, page, or window geometry. Preserve stable page regions, retain current content during background refresh, and use skeletons or local progress only for an unresolved initial load without replacing the authenticated shell. Context transitions make stale product content inert immediately, retain it visually for a fast refresh, obscure it with a neutral fixed-geometry surface only after 500 milliseconds, keep the account context view mounted with safe choices, and close unrelated transient surfaces.
- Empty collection: keep page hierarchy and relevant controls visible, explain the empty state briefly, and offer only an authorized next action.
- Forbidden or missing resource: fail closed, avoid disclosing protected identity or policy detail, and remove unavailable mutation controls.
- Unavailable dependency: distinguish the temporary unavailable state from forbidden and missing states and provide a safe retry where retry can help.
- Validation or conflict: keep recoverable input in its managed window, identify the affected field or decision, and avoid resetting sibling windows or collection state.
- Compact viewport: collapse or wrap secondary toolbar controls, retain essential table context with internal scrolling, and render managed windows fullscreen without document overflow.
- Specialized workflow: retain the same tokens, state roles, accessibility, and feedback patterns while using a documented Dedicated Workbench anatomy instead of forcing a data table or window.
- Visual exception: stop consumer implementation, document why the existing theme, primitive, app pattern, or archetype cannot express the need, and obtain explicit approval before adding a cross-feature convention.

## Acceptance Criteria

- **AC-001** Axis uses one visual character across features: calm, precise, data-first, moderately dense on desktop, compact but touch-safe on small viewports, with no page-local marketing decoration, gradients, glass effects, arbitrary card grids, or decorative motion.
- **AC-002** Visual ownership is unambiguous: semantic values come from `theme/axis-theme.json`, reviewed primitive visuals come from the upstream zone, reusable composition comes from shared app patterns, and feature code changes outer layout only.
- **AC-003** Typography uses the theme heading family for page, section, and component titles and the theme body family for controls and content. Page titles use a 24-pixel semibold role, section titles an 18-pixel medium role, component titles a 16-pixel medium role, standard body and desktop controls a 14-pixel role, and metadata a 12-pixel role; compact form controls retain a 16-pixel text role where needed to avoid mobile zoom.
- **AC-004** Layout uses a 4-pixel base rhythm with primary spacing steps of 8, 12, 16, 24, and 32 pixels; route padding is 16 pixels on compact, 24 pixels on standard, and 32 pixels on wide viewports. Standard desktop controls use the shared compact height, every compact touch control exposes at least a 44-by-44-pixel hit area, and dense tables remain readable without making unrelated pages sparse.
- **AC-005** Radius, elevation, icons, contrast, and motion use the measurable visual-role table above; feature code neither introduces another tier nor weakens its accessible threshold.
- **AC-006** Interaction states use the shared hierarchy: `accent` for transient hover or keyboard highlight, `secondary` for persistent selection or current state, the semantic focus ring independently of fill, and semantic feedback pairs for informational, success, warning, and destructive meaning. Persistent state remains stronger than transient state in light and dark modes. Selectable menu options keep their semantic icon at the inline start and express the current choice through persistent fill without a trailing checkmark. Async surfaces lock interaction and expose `aria-busy` immediately, delay visual pending feedback, render it only in a geometry-stable slot, preserve current content during background refresh, and let ready, error, or recovery feedback replace pending state immediately. Context transitions keep their owning account view mounted, update safe selection state in place, close unrelated transient surfaces, and never replace the work area with an informational notice.
- **AC-007** A management route defaults to the Resource Workspace composition: one clear page title and optional concise description, one contextual status region, and the [Collection Page](../data-display/collection-page.md) plus [Data Table](../data-display/data-table.md) foundations. It adds no competing detail card grid or feature-owned action column.
- **AC-008** Long-lived record tasks compose the [Managed Dialog](../overlays/managed-dialog.md) foundation. Alert dialogs own bounded destructive or dirty confirmations; popovers and menus own immediate contextual choices. This contract tests the proving-consumer integration and does not duplicate the mechanics or evidence of those foundations.
- **AC-009** A Dedicated Workbench is used only for a documented canvas, drag-and-drop builder, multi-record comparison, long-running process, or several dependent panels that cannot remain usable in a managed window. Entry and informational pages may use a simpler anatomy but retain the same visual and state foundations.
- **AC-010** Every archetype defines complete, loading, empty, forbidden, missing, unavailable, validation, conflict, disabled, and mutation states relevant to its workflow without changing page geometry unnecessarily or exposing raw technical detail.
- **AC-011** Responsive behavior preserves a fixed authenticated shell and assigns scrolling to explicit route, table, or window regions; toolbars wrap or collapse secondary actions, essential table context remains available, compact managed windows become fullscreen, and no supported viewport introduces document or horizontal page overflow.
- **AC-012** Keyboard access, visible focus, accessible names, semantic landmarks, screen-reader reading and focus order, async status announcements, contrast, reduced motion, pointer and touch behavior, and localized-copy fit are verified in light and dark modes. Functional motion follows the visual-role table and never becomes decorative reveal.
- **AC-013** The first proving consumer is a Business Objects Resource Workspace that demonstrates the complete state matrix, a primary data table, and at least two independently managed record windows across desktop and compact viewports.
- **AC-014** After the proving consumer is frozen, each migrated consumer composes the frozen shared patterns for one approved archetype without creating a local alternative.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Static frontend | Source-boundary checks keep semantic values in the theme, reviewed visuals in primitives, composition in shared patterns, and feature styling limited to semantic outer layout. | AC-001, AC-002, AC-005, AC-006 | Frontend CI | Yes |
| AT-002 | UI component | Focused shared-pattern tests assert the exact typography, spacing, touch target, radius, elevation, icon, state, announcement, focus-order, contrast, motion, and reduced-motion roles in light and dark modes. | AC-003, AC-004, AC-005, AC-006, AC-012 | UI component test | Yes |
| AT-003 | UI component | The proving Resource Workspace renders its complete state matrix, one primary table, toolbar composition, stable geometry, and two independent managed-task integrations without an action column. | AC-007, AC-008, AC-010, AC-013 | UI component test | Yes |
| AT-004 | Layout smoke | Light and dark proving-consumer snapshots at desktop and compact widths retain the calm data-first character, hierarchy, Vietnamese copy fit, touch targets, internal scrolling, and stable page geometry without page overflow; explicit user visual acceptance closes the subjective visual judgment. | AC-001, AC-004, AC-005, AC-010, AC-011, AC-012, AC-013 | Browser-capable visual smoke | Yes |
| AT-005 | Browser journey | A focused proving-consumer journey covers pointer and keyboard launch, two-window overlap and focus, minimize and restore, dirty closure, async announcements, focus transitions, and draft survival across authenticated navigation. | AC-008, AC-010, AC-011, AC-012, AC-013 | Browser automation | Yes |
| AT-006 | UI component | A migrated consumer composes one approved archetype and the frozen shared patterns without a feature-local visual alternative. | AC-007, AC-009, AC-014 | UI component test | Yes |

## Out Of Scope

- Replacing semantic values owned by `theme/axis-theme.json` or duplicating those values in feature code.
- Reimplementing mechanics already owned by the data-table, collection-page, managed-window, status, navigation, or interaction-state contracts.
- Forcing dashboards, authentication screens, drag-and-drop builders, canvas editors, comparison views, or long-running operations into the Resource Workspace anatomy.
- Persisting managed windows or drafts across browser reloads, signed-in sessions, or devices.
- Migrating existing product surfaces before the proving consumer receives explicit visual acceptance.

## Screen flow

| Archetype or layer | Required contract |
|---|---|
| App shell | Keep authenticated navigation and utility controls visually stable; route content owns its explicit work region and never creates a competing shell. |
| Resource Workspace | Render page heading, optional concise guidance, contextual status, one primary data table, and a toolbar action area. Preserve table state while record workflows use managed windows. |
| Managed Task Window | Use the managed-window header, internally scrolling body, and stable footer; keep independent query, draft, dirty, busy, and error state for each stable identity. |
| Dedicated Workbench | Use a route-sized work area for an approved complex workflow while retaining theme, hierarchy, interaction states, feedback, responsive behavior, and accessibility. |
| Entry / Informational | Use a simpler focused anatomy for authentication, onboarding, or durable information without importing marketing-page decoration into the product workspace. |
| Alert dialog | Ask one bounded destructive, dirty-state, or irreversible confirmation and return focus to its trigger or owning window. |
| Popover or menu | Offer immediate contextual selection or commands without becoming a long-lived task surface. |

The golden reference must show complete, loading, empty, forbidden, missing, unavailable, validation, conflict, saving, and success states where applicable. It must also show light and dark modes, desktop and compact viewports, pointer and keyboard interaction, two open record windows, focus changes, minimize and restore, dirty-close confirmation, and draft preservation across authenticated navigation.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Done |
> | Tests | Done |
>
> **Implemented:** The durable contract, ownership layers, page-archetype decisions, golden-reference scope, state matrix, and clean-cutover gate are implemented. Shared async patterns own timing and geometry; registry selection primitives own persistent secondary fill without a trailing checkmark; `ResourceWorkspace` owns management-page anatomy; Business Objects remains the golden reference; and the app shell owns one recoverable context-transition coordinator. After explicit visual acceptance, `frontend/ui-foundation.json` records phase `frozen`, and Auth, Invitations, Memberships, Product Roles, Service Identities, Rules, Solutions, and Business Objects consume the approved owners without compatibility paths.
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** New specialized workbenches still require the documented exception decision before implementation; this is future product scope, not unfinished foundation work.
>
> **Verification:** Phase and UI-policy regressions prove the frozen boundary, async ownership, page anatomy, and primitive selection contract. Frontend compile/lint and the focused Auth, invitation, management, Rules, Solutions, Business Objects, shared async, option, DataTable, and primitive suites pass. Browser evidence passes the accepted light/dark desktop/compact golden matrix, independent managed windows, and both Workspace directions without document or account-menu scroll drift. The approved UI baseline records the reviewed registry changes.
>
> **Decisions:** Business Objects is the proving Resource Workspace because it combines a collection table, record actions, permission states, and managed windows; its authenticated shell proves the account/workspace context boundary. Explicit visual acceptance on 2026-08-10 froze the contract. Existing theme values, registry primitives, semantic interaction patterns, page archetypes, and the app shell remain their single owners; feature code supplies business state and copy only.
