# Frontend Playbook

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use `$axis-frontend-feature` for SPA feature work. Use `$axis-frontend-foundation` for app shell or shared SPA foundation work owned by [docs/foundations/README.md](../foundations/README.md).

## UX-first product UI

Build the workflow, not a landing page or explanation page. Visible copy should help users act and avoid internal architecture terms. On localized surfaces, user-facing product copy must use the frontend translation layer instead of component-local static text. Keep non-product constants, routes, and protocol values separate from visible copy.

Every route must expose an obvious next navigation path. Auth and public standalone screens declare route-level escape targets with `routeNavigation = publicRouteNavigation(...)` and render a visible sign-in, registration, back, or home-style link in every loading, success, and error state. Redirect-only route entries that render no screen state are exempt. Authenticated screens satisfy this through the app shell navigation and sign-out.

Technical handoff routes should complete successful handoffs before rendering standalone UI. Use visible handoff screens only when the user needs to read a durable result, wait for a deliberately held confirmation, or recover from an error.

## Mobile-first layout and radius

Design from small screens up. Keep cards and controls at restrained radius unless the owning use case says otherwise.

For localized surfaces, validate copy fit in the supported languages named by the owning use case at mobile and desktop sizes. Prefer responsive layout capacity and design-system improvements over language-specific copy or styling hacks.

Fixed-shell product screens must not create document or app-shell scrolling. Constrain overflow to explicit internal regions, and give dense repeat-edit regions a focus/maximized state when they need more working area while keeping authenticated navigation context visible.

## Feature folder anatomy

Feature code lives under `frontend/src/features/{name}/` and exports through the feature index. Avoid cross-feature deep imports.

## State management

Use TanStack Query for server state. Use local React state or Zustand only for client-only state.

## TanStack Query patterns

Use stable query keys, generated API types, and explicit invalidation after mutations.

Route-owned server state uses query option factories shared by route loaders and components. Initial data needed for first screen paint should load through a TanStack Router loader with `queryClient.ensureQueryData`; use `prefetchQuery` for intent-based warming where rendering must not wait. Router preload only warms business data when a route loader exists.

Prefetch detail, adjacent page, or navigation data on clear user intent such as hover, focus, selection, or route navigation. Keep auth, acceptance criteria, data sensitivity, and request cost in the decision; do not prefetch expensive or sensitive data only because a component renders.

Mutation success handlers should write returned entities into exact detail cache entries and explicitly invalidate or update affected list keys. Use broad feature-prefix invalidation only when multiple known projections intentionally become stale.

Put shareable pagination, filters, and selected record identifiers in route search params. Keep local state for draft-only UI state that should reset on navigation or reload.

## TypeScript and server-owned values

Use strict TS with no `any` and generated API types for backend contracts. Frontend forms submit user-authored decisions and required protocol tokens only. If a value is derived by the system, the UI may show an advisory read-only preview, but the generated API request type must exclude that value and the response remains the source of truth.

Backend-owned business vocabulary follows the system-wide source-ownership contract in [client-experience.md](./client-experience.md#server-owned-product-vocabulary). Consume generated reference metadata directly for labels, selected values, guidance, examples, and compatibility. Do not reproduce backend enum or capability meaning with frontend switches, interpolated translation keys, or static documentation arrays. Generic UI copy remains client-owned and localized.

## Routing

Use TanStack Router patterns already in the app. Classify every route as authenticated, guest-only, or public-neutral before adding it. Protected routes live under the authenticated layout; guest-only auth or registration routes live under the guest-only layout; public-neutral routes stay outside both access groups.

## Component design

Follow the contract lifecycle and one-way [UI architecture](../foundations/visual-system/axis-visual-system.md#architecture). `frontend/ui-foundation.json` records enforced spec/evidence metadata; its contract keys become the TypeScript contract-id union. `frontend/src/lib/ui-foundation.ts` binds each finite active surface id to that union, while `frontend/src/lib/active-surface-registry.ts` exhaustively inventories real owner and implementation symbols. Surface owners require the compatible typed id, emit rendered contract metadata, and are kept feature/route independent by Biome's parsed import rule; filename conventions, path strings, and raw-source scans are not conformance. A new consumer of an unchanged contract does not need bespoke visual approval, but it does need typed registration plus focused rendered and product-state evidence. Run `python scripts/axis.py check ui-foundation` and `python scripts/axis.py frontend ci` for every frontend UI change.

Choose semantic meaning before a component. Product UI uses an accessible reviewed primitive when one maps that meaning; native fallback behavior requires an accepted platform need. Selected values and options use the same display-label source, icons support rather than replace accessible names, and fixed controls retain stable geometry.

Treat component visuals as mappings of the constitution, not independent conventions. Feature code uses defaults and documented props; it does not locally alter visual treatment through style overrides, selectors, or wrapper styling. If no semantic role fits, return to `$axis-ui-system` before implementation instead of creating a component-local rule.

Use these ownership layers:

- **Upstream zone** — `frontend/src/components/ui` plus baseline-tracked shadcn support files contain reviewed registry source. Keep registry-default visuals and APIs; do not add product variants, business logic, feature/shared imports, or internal styling. `frontend/ui-baseline.json` records the approved snapshot and the reason/sign-off reference for each explicit exception.
- **Theme zone** — [theme/axis-theme.json](../../theme/axis-theme.json) owns reusable color, typography, spacing, density, radius, elevation, icon, motion, and layer values. Generated CSS and runtime TypeScript are committed; `frontend/src/index.css` owns imports and base styles only. Consumers use semantic roles rather than copying current values.
- **App-pattern zone** — `frontend/src/components/shared` owns reusable Axis composition and adapters. Give a surface owner narrow semantic props: leaf content stays inside owner-rendered anatomy; regions with their own states, relationships, or actions use typed models rendered by the owner.
- **Feature zone** — feature components compose defaults and app patterns. They provide product data, state, and commands; they do not pass visual or mechanical capability across a surface boundary.

Dependency flows downward through the constitution, theme, upstream primitives, app patterns, page archetypes, app shell, and feature composition. Features do not own reusable values, global timing, scroll containers, overlay mechanics, focus recovery, control visuals, or alternate page anatomies. The app shell owns stable viewport and context-transition mechanics; the active page archetype owns route geometry; the feature owns product state and composition.

[docs/playbooks/client-experience.md](./client-experience.md) owns the self-directed experience contract, view hierarchy, relationship selection, semantic component choice, and pre-JSX audit. Feature code must not import raw `Badge`.

The [Axis UI Constitution](../foundations/visual-system/axis-visual-system.md#interaction-state-model) owns the single interaction-state hierarchy and async grammar. Registry primitives and app patterns map those roles; `frontend/src/components/shared/interactionStates.ts` is only a code adapter. Compare equivalent meaning across overlays, navigation, collections, forms, and feedback; do not turn an implementation detail into durable guidance.

[docs/playbooks/client-experience.md](./client-experience.md#semantic-component-selection) owns semantic feedback, state-label, metadata, relationship, and action selection. Feature code consumes those meanings and never imports raw provider components to create another vocabulary.

Use `$axis-ui-system` for constitution/theme decisions, registry diffs, baseline refreshes, or provider changes. Never bulk-overwrite unrelated registry components. A reviewed upstream sync and matching baseline refresh need provenance when they introduce no customization; constitution, semantic-value, provider, primitive-exception, or cross-feature convention changes require the Design Gate and applicable sign-off.

## Styling

Use Tailwind utilities consistently. Outside the upstream and theme zones, use semantic tokens and standard utilities only: no hard-coded palette utilities, arbitrary Tailwind values, component-local colors, selector-based custom CSS, or one-off visual systems. Remove obsolete styling and component API surface when the UI path that used them is removed or replaced.

## Security

Do not store auth tokens in `localStorage`. Treat permission/visibility behavior as product behavior with tests.

## Performance

Keep heavy canvas and builder interactions scoped, virtualized, or memoized when needed. Test visible behavior first.

## Accessibility baseline

Keyboard, focus, labels, error text, and disabled/loading states must be observable and testable.
