# Axis UI Constitution

> **Navigation**: [docs/foundations/visual-system/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Define one product-level visual and interaction grammar so Axis remains coherent as features and component implementations change. Components conform to this constitution; component names, markup, and provider APIs are not design policy.

## Consumers

- Every authenticated, public, entry, authoring, administration, and product-runtime surface.
- Theme generation, upstream primitives, shared app patterns, feature composition, review, and frontend policy.
- Future component providers and specialized workbenches that must preserve the same semantic roles.

## Activation

- Any change to visible hierarchy, interaction state, feedback, density, typography, color, spacing, surface, motion, layering, responsive behavior, scroll ownership, or page anatomy.
- Any new or replaced primitive, shared visual API, page archetype, component provider, or cross-feature convention.

## Guarantees

- Axis is a calm, precise, data-first enterprise workspace. Neutral hierarchy carries structure; brand color marks primary intent; semantic color communicates meaning.
- [theme/axis-theme.json](../../../theme/axis-theme.json) is the sole machine-readable source for reusable visual values and timing. The generated CSS and runtime projection are the only implementation inputs for those values.
- This constitution owns semantic roles and invariants. Upstream primitives own accessible mechanics; app patterns map roles to reusable composition; features own product state, content, relationships, and outer layout only.
- Equivalent meaning looks and behaves equivalently across navigation, tables, forms, menus, windows, dialogs, and compact or desktop layouts.
- A component may change provider, markup, or internal implementation without changing product meaning. A component-specific test proves its mapping; it cannot establish a new product convention.

## Semantic grammar

| Axis | Roles | Invariant |
|---|---|---|
| Hierarchy | page, section, component, body, label, metadata | Use typography, spacing, and contrast before adding containers or decoration. One level has one role across the product. |
| Space | inline, region, section, page-compact, page-default, page-wide | Use the shared rhythm by relationship. A feature does not invent reusable gaps or page gutters. |
| Density | compact-control, default-control, touch-target | Desktop density stays efficient; pointer size never weakens the compact touch target. |
| Surface | base, floating, managed | Base content uses structure without elevation; contextual overlays float; long-lived tasks use managed elevation. |
| Radius | flat, control, floating, managed | Radius communicates boundary depth, not decoration. Features do not choose a new tier. |
| Elevation | none, floating, managed, dock | Elevation follows surface depth and remains equivalent in light and dark modes. |
| Icon | control, navigation, empty | Use one vector family and one size/stroke role at each hierarchy. Icons support meaning and never replace an accessible name. |
| Motion | state, floating, pending, context | Motion communicates causality, uses opacity or transform, is interruptible, and respects reduced motion. |
| Layer | base, sticky, floating, modal, managed, notification | Layer order is semantic and finite; components do not invent numeric stacking values. |

Exact role values live only in [theme/axis-theme.json](../../../theme/axis-theme.json). Product code consumes generated semantic names rather than copying their current numbers.

## Interaction state model

| State | Product meaning | Required expression |
|---|---|---|
| Rest | Available and inactive | Owning surface and foreground pair. |
| Transient | Pointer hover, keyboard highlight, or open context | Lower-emphasis transient surface; never replaces persistent state. |
| Persistent | Selected, current, expanded, or toggled | Stronger persistent surface retained through hover. Choice semantics determine whether a native indicator is also required. |
| Focus | Keyboard position | Visible semantic ring independent of fill, color, and selection. |
| Disabled | Unavailable action | Semantic disabled state and native non-interactive behavior; never opacity alone. |
| Busy | Authoritative work in progress | Lock only the affected boundary immediately; delay visual feedback; keep geometry and labels stable. |
| Destructive | Irreversible or harmful consequence | Destructive semantic pair plus explicit language; never color alone. |
| Feedback | Information, success, warning, or failure | Semantic pair, text, accessible announcement, and recovery when recovery exists. |

Initial loading reserves the owning region and does not render empty content first. Background refresh preserves current content and scroll position. User actions keep labels and bounds stable. Context transitions keep the authenticated frame and account context mounted, make stale content inert, and obscure it only after the shared delay. Fast operations therefore complete without spinner flashes, layout shifts, or transient document scrollbars.

## Layout and composition

- The authenticated frame owns the viewport and global navigation. Each route has exactly one page archetype and one scroll owner.
- Resource management defaults to one primary collection with long-lived create, view, and edit tasks in app-scoped managed windows.
- Overview pages summarize outcomes and navigation; they do not imitate management tables.
- A dedicated workbench requires a canvas, builder, comparison, long-running process, or dependent panels that cannot remain usable in a managed window.
- Entry and informational pages may use a focused anatomy while retaining the same semantic roles and state model.
- Compact layouts preserve task priority, touch targets, labels, and recovery; desktop layouts increase density without changing meaning.

## Conformance and exceptions

1. Select the semantic role before selecting a component.
2. Reuse an accessible upstream primitive or existing app pattern when it maps the role.
3. Keep component internals and provider variants local to their owner; keep feature styling to outer relationship layout.
4. When no role fits, stop. Extend this constitution and the canonical theme only if the need is cross-feature; otherwise record an explicitly accepted bounded exception.
5. Remove the retired mapping when replacing a convention. Do not keep parallel visual paths, compatibility wrappers, or feature-local fallbacks without a real supported consumer constraint.

## Phase model

| Phase | Meaning | Allowed work |
|---|---|---|
| `requested` | Direction proposed | Read-only discovery. |
| `authorized` | Design Gate and required sign-off current | Constitution and value design. |
| `defined` | Constitution and semantic values are current | Golden-reference implementation only. |
| `reference-ready` | Running golden reference and focused browser evidence exist | User visual review; no unrelated migration. |
| `accepted` | User accepted the running reference | Bounded active-surface migrations. |
| `adopted` | Every active supported surface conforms with current evidence | Completion claim and normal feature consumption. |

Acceptance freezes the direction; it does not claim system-wide adoption. Any accepted direction change returns to `defined` and invalidates affected browser evidence.

## Alternate / error flows

- Loading, empty, forbidden, missing, unavailable, validation, conflict, disabled, pending, success, and stale/retry states retain the same hierarchy and geometry wherever they apply.
- Permission and missing states remain non-disclosing. Unavailable states are distinguishable and recoverable when retry can help.
- Long-lived drafts survive supported navigation and window focus changes; dirty dismissal is explicit.
- Reduced motion removes spatial transition while preserving immediate state and focus. Localization may change copy length but not role, hierarchy, or behavior.
- Unsupported compact geometry, contrast, focus, overflow, or provider behavior blocks acceptance or adoption; it is not deferred as polish.

## Acceptance Criteria

- **AC-001** One semantic constitution and canonical theme own every reusable visual value, interaction role, timing, and layer; no component or feature creates a parallel convention.
- **AC-002** Equivalent hierarchy, state, feedback, density, surface depth, icon role, and motion remain perceptually equivalent in light/dark and desktop/compact modes.
- **AC-003** Async initial load, refresh, action, and context transition preserve geometry, content continuity, scroll ownership, focus, and accessible status without fast-operation flashing.
- **AC-004** Every route uses one approved archetype and one scroll owner; resource management is collection-first with managed task windows unless an accepted workbench exception applies.
- **AC-005** Keyboard, screen reader, contrast, localization, touch target, reduced motion, responsive overflow, and focus recovery meet the enterprise accessibility baseline.
- **AC-006** The phase machine separates definition, running reference, visual acceptance, and complete adoption; status and evidence never advance ahead of the current phase.
- **AC-007** Component/provider replacement preserves semantic roles and removes the retired mapping unless an evidence-backed compatibility constraint exists.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Static frontend | Strict semantic schema deterministically projects CSS and runtime values and rejects missing, unknown, or stale roles. | AC-001, AC-002, AC-003 | Frontend CI | Yes |
| AT-002 | Static frontend | Ownership checks reject hard-coded semantic values, provider leakage, feature-local interaction visuals, and manifest/status drift. | AC-001, AC-006, AC-007 | Frontend CI | Yes |
| AT-003 | UI component | Representative role mappings prove hierarchy, state priority, async geometry, scroll ownership, accessibility, and reduced motion without establishing component-local policy. | AC-002, AC-003, AC-004, AC-005 | UI component test | Yes |
| AT-004 | Layout smoke | The golden resource workspace proves complete states in light/dark and desktop/compact layouts without document overflow or console errors. | AC-002, AC-004, AC-005 | Browser-capable visual smoke | Yes |
| AT-005 | Browser journey | Pointer and keyboard task flow proves independent managed work, focus, draft, pending, recovery, navigation, and context continuity. | AC-003, AC-004, AC-005 | Browser automation | Yes |
| AT-006 | Static frontend | Active-surface inventory and focused evidence prove every supported surface maps the accepted roles with no parallel convention or unresolved exception. | AC-001, AC-006, AC-007 | UI component test + Frontend CI | Yes |

## Out Of Scope

- Product-specific content, workflow, authorization, or business state.
- Server-defined product vocabulary or remote layout schemas.
- A permanent component catalog, provider API reference, or rule for every component name.
- Forcing specialized builders, canvases, reporting, or future experiences into a resource workspace.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Contract | Done |
> | Frontend | Partial |
> | Tests | Partial |
>
> **Gaps vs spec:** Explicit user visual acceptance and complete active-surface adoption remain.
>
> **Deferred follow-ups:** N/A; acceptance and adoption are current required phases, not deferred work.
>
> **Verification:** Current verification is recorded in [docs/foundations/visual-system/axis-visual-system.evidence.md](./axis-visual-system.evidence.md); stale evidence from the retired contract does not advance the phase.
>
> **Decisions:** The constitution owns semantic invariants, the canonical theme owns reusable values, and components remain replaceable mappings. `frontend/ui-foundation.json` records phase plus golden archetype/route only. Business Objects remains the proving resource workspace; the Account clean cutover and fresh browser evidence return the phase to `reference-ready` for user visual review.
