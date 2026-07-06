# Module Navigation

> **Navigation**: [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provide the global authenticated sidebar navigation contract for Axis Platform modules without owning module product behavior or extension lifecycle behavior.

## Primary actor

- Signed-in Axis Platform user

## Trigger

- User opens an authenticated Axis Platform route after at least one visible module navigation contribution is available.

## Main flow

1. Module-owned code supplies navigation contributions through the module navigation contribution contract.
2. System filters contributions by visibility, permission, and availability rules supplied by the contribution owner.
3. System renders a global navigation sidebar only when at least one visible contribution remains.
4. System groups, orders, labels, and marks active contributions consistently across authenticated routes.
5. User selects a contribution and system routes to that module-owned destination.
6. System preserves the owning route content region without imposing module-specific page layout.

## Alternate / error flows

- No visible contributions: App Shell renders no sidebar or placeholder sidebar.
- Narrow viewport: navigation uses a mobile-appropriate global navigation affordance without horizontal page overflow.
- Contribution points to an unavailable or unauthorized destination: the contribution is not exposed as a navigable item.
- Contribution metadata is invalid or incomplete: the contribution is ignored and the fault is surfaced through developer diagnostics, not user-facing placeholder UI.

## Acceptance Criteria

*Contribution contract*
- **AC-001** Module navigation contributions declare a stable module or feature identity, localized label key, icon token, route target, grouping, ordering, active matching rule, and visibility metadata.
- **AC-002** App Shell consumes module navigation contributions through a shared registry or equivalent extension point instead of hard-coding module-specific sidebar items.
- **AC-003** Contribution owners define route targets and visibility rules; App Shell only renders contributions that are visible and navigable for the current user and route context.

*Frame behavior*
- **AC-004** App Shell renders the global sidebar only when at least one visible contribution exists.
- **AC-005** The sidebar exposes grouped, ordered, keyboard-reachable navigation with localized labels, recognizable icons, and active route state.
- **AC-006** The sidebar fits supported desktop and mobile widths without horizontal page overflow.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Empty contribution set renders no sidebar or placeholder navigation. | AC-004 | UI component test | Yes |
| AT-002 | UI component | Visible contributions render in group/order with localized labels, icons, route targets, and active state. | AC-001, AC-002, AC-005 | UI component test | Yes |
| AT-003 | UI component | Hidden, unauthorized, unavailable, or invalid contributions are not exposed as navigable sidebar items. | AC-003 | UI component test | Yes |
| AT-004 | Browser journey | Desktop and mobile navigation render without console errors or horizontal overflow. | AC-006 | Browser automation | Yes |
| AT-005 | Static frontend | Contribution registry and renderer typecheck, lint, and keep localized copy keys valid. | AC-001, AC-002 | Frontend CI | Yes |

## Out Of Scope

- Implementing the first module destination or module product workflow.
- Canvas-specific tool, layer, property, asset, or inspector panels.
- User-created extension packaging, runtime loading, sandboxing, marketplace, or installation lifecycle.
- Backend permission model definitions beyond consuming visibility metadata supplied to the renderer.
- Route-specific contained, fluid, or canvas workspace layout decisions.
- Rendering an empty or placeholder sidebar before visible contributions exist.

## Screen flow

| Screen | Required contract |
|---|---|
| Authenticated app frame without contributions | Render top bar, full-width main content, and footer without a sidebar placeholder. |
| Authenticated app frame with contributions | Render global module navigation beside the route content on supported desktop widths. |
| Narrow viewport | Expose global module navigation through a mobile-appropriate affordance without horizontal overflow. |
| Module contribution item | Show localized label, icon, active state, and route target owned by the contributing module. |
| Hidden or unavailable contribution | Do not expose the item as navigable UI. |

Required UI quality: navigation controls must be keyboard-reachable, labels must be localized, active state must be observable, and the sidebar must not reduce route content to a fixed-width dashboard layout.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Contract | Done |
> | Frontend | Not started |
> | Tests | Not started |
>
> **Implemented:** Contract only.
>
> **Gaps vs spec:** No module navigation contribution registry, sidebar renderer, responsive navigation affordance, contribution tests, or browser evidence exists yet.
>
> **Deferred follow-ups:** Implement this foundation when the first real module navigation contribution exists.
>
> **Verification:** Contract shape is covered by `python scripts/axis.py check foundation-docs`; runtime acceptance tests are not present because frontend implementation has not started.
>
> **Decisions:** Module Navigation is an App Shell foundation, not a use case. App Shell owns the global navigation renderer and empty behavior; modules own the contributed items, labels, routes, ordering hints, and visibility rules. The sidebar must not render when there are no visible contributions. User-created extension mechanics are future platform extensibility contracts; this foundation only defines the navigation boundary they will contribute into.
