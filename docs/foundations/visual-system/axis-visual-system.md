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
- Requires one accepted golden reference before cross-feature migration and uses that frozen reference as the visual comparison target for later consumers.

Visual roles are measurable and owned once:

| Dimension | Required role | Owner / value |
|---|---|---|
| Semantic color and fonts | Background/foreground pairs, focus, status, heading, and body families | Exact values come only from `theme/axis-theme.json` and its generated projection. Contrast is at least WCAG AA: 4.5:1 for normal text and 3:1 for large text, UI boundaries, and meaningful non-text state. |
| Radius | Flat; control; compact floating; task overlay | Collection tables use `rounded-sm`; controls use the reviewed primitive default with an 8-pixel theme base; popovers, menus, and select lists use `rounded-lg`; alert dialogs, managed windows, docks, and switchers use `rounded-xl`. Features do not choose another radius tier. |
| Elevation | Base; compact floating; managed | Routes, cards, forms, and tables use border or ring with `shadow-none`; popovers, menus, and select lists use `shadow-md` plus their reviewed ring; bounded dialogs and managed windows use at most `shadow-lg` plus a one-pixel `foreground/10` ring; docks and switchers use at most `shadow-xl` plus the same ring. |
| Icons | Control; navigation; empty state | Lucide is the only product icon family. Control icons are 16 pixels, navigation icons are 20 pixels, and empty-state icons are 24 pixels. Icon-only controls have an accessible name and tooltip where the action is not otherwise visible. |
| Motion | State; compact floating; direct manipulation | Color, border, and opacity state changes use 150ms ease-out; compact floating surfaces enter and exit in 100ms; drag and resize track the pointer without easing. Reduced motion removes slide, zoom, and spatial transition while retaining immediate state, focus, and at most 100ms opacity feedback. |

## Alternate / error flows

- Loading: preserve stable page regions and use skeletons or local progress without replacing the entire authenticated shell.
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
- **AC-006** Interaction states use the shared hierarchy: `accent` for transient hover or keyboard highlight, `secondary` for persistent selection or current state, the semantic focus ring independently of fill, and semantic feedback pairs for informational, success, warning, and destructive meaning. Persistent state remains stronger than transient state in light and dark modes.
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
> | Frontend | Partial |
> | Tests | Partial |
>
> **Implemented:** The durable contract, ownership hierarchy, page archetypes, golden-reference scope, state matrix, and migration gate are defined. Shared page composition and interaction-state owners are implemented. Business Objects is the accepted and frozen proving Resource Workspace; Rules, Memberships, Product Roles, and Service Identities are migrated consumers of that composition. Each management consumer keeps one primary table while moving record, mutation, and lifecycle work into app-scoped managed windows.
>
> **Gaps vs spec:** The approved Resource Workspace migration set is complete. Solutions retains its documented long-running lifecycle needs and requires a Dedicated Workbench review instead of a forced table/dialog conversion; entry and informational surfaces still require their separate anatomy review.
>
> **Deferred follow-ups:** Migrate the remaining Resource Workspaces in bounded feature-owned waves, review Dedicated Workbench and entry/informational surfaces against their approved anatomies, and remove superseded local composition only as each consumer moves.
>
> **Verification:** Focused shared-pattern, Business Objects, Rules, Memberships, Product Roles, and Service Identities component evidence covers the implemented composition, state behavior, managed drafts, exact mutations, and guarded closure. Browser evidence covers the proving consumer's light/dark desktop/compact matrix and managed-window journey. Explicit visual acceptance freezes that proving consumer as the comparison target for subsequent migration; focused Memberships, Product Roles, and Service Identities browser journeys remain their runtime checks.
>
> **Decisions:** Business Objects is the frozen proving Resource Workspace because it combines a collection table, record actions, permission states, and managed windows. Rules is the first follow-on Resource Workspace because its existing grid-first workflow already fits the approved archetype without behavioral redesign. Existing theme values and focused foundation mechanics remain their single owners; this contract defines how they compose into the coherent product experience. Explicit visual acceptance closes the subjective freeze gate but does not replace automated behavior and accessibility evidence.
