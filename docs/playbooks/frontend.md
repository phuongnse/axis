# Frontend Playbook

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use `$build-frontend` for SPA feature work. Use `$build-frontend-foundation` for app shell or shared SPA foundation work owned by [docs/foundations/README.md](../foundations/README.md).

## UX-first product UI

Build the workflow, not a landing page or explanation page. Visible copy should help users act and avoid internal architecture terms. On localized surfaces, user-facing product copy must use the frontend translation layer instead of component-local static text. Keep non-product constants, routes, and protocol values separate from visible copy.

Frontend UI, accessible-name, and perceptual evidence uses the product `DEFAULT_LANGUAGE` (`en`) as its sole canonical copy locale. Assert literal copy only when that English copy or accessible name is the contract under test; interaction tests use roles, states, relationships, and fixture-owned business values. Localization-owner tests may exercise other supported locale identifiers to prove catalog parity, selection, persistence, and document state, but do not assert secondary-locale wording or create per-locale screenshots.

Every route must expose an obvious next navigation path. Auth and public standalone screens declare route-level escape targets with `routeNavigation = publicRouteNavigation(...)` and render a visible sign-in, registration, back, or home-style link in every loading, success, and error state. Redirect-only route entries that render no screen state are exempt. Authenticated screens satisfy this through the app shell navigation and sign-out.

Technical handoff routes should complete successful handoffs before rendering standalone UI. Use visible handoff screens only when the user needs to read a durable result, wait for a deliberately held confirmation, or recover from an error.

## Mobile-first layout and radius

Design from small screens up. Keep cards and controls at restrained radius unless the owning use case says otherwise.

Localized surfaces remain structurally responsive to variable copy and must not introduce language-specific layout or styling branches. Canonical mobile and desktop UI evidence remains English-only.

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

Follow the project-wide [Enterprise Production Baseline](../PLATFORM_STRATEGY.md#enterprise-production-baseline), the contract lifecycle, and one-way [UI architecture](../foundations/visual-system/axis-visual-system.md#architecture). UI conformance specializes the visible-experience portion of production fitness and cannot establish overall readiness by itself. Declare one review unit before implementation: constitution/foundation, one surface owner, or one consumer of an unchanged enforced owner. Use the largest coherent owner/contract/invalidation/review boundary—normally the whole foundation/surface or a complete consumer—and do not split it by typography, spacing, or individual region unless the resulting units are genuinely independent in ownership, acceptance, verification, or rollback. `frontend/ui-coverage-profile.json` defines the current versioned UI requirements, evidence kinds, representative modes, and invalidation triggers; its count is not a universal checklist. `frontend/ui-foundation.json` records each defined-or-later contract's profile, lifecycle and review state, declared evidence, and exact covered/gap/not-applicable partition. Covered requirements must trace to real acceptance-test ids, matching evidence-sidecar rows, declared evidence, and required modes; profile gaps block `verified` and `enforced`. Contract keys become the TypeScript contract-id union and the checked `enforcedContracts` registry becomes the enforced-contract union. `frontend/src/lib/ui-foundation.ts` binds each finite active surface id to that catalog, while `frontend/src/lib/active-surface-registry.ts` exhaustively inventories real owner and implementation symbols. Surface owners require the compatible typed id, emit rendered contract metadata, and are kept feature/route independent by Biome's parsed import rule; filename conventions, path strings, screenshots, and raw-source scans are not conformance. A new consumer of an unchanged enforced contract does not need bespoke owner approval, but it does need typed registration plus focused rendered and product-state evidence. Run `python scripts/axis.py check ui-foundation` and `python scripts/axis.py frontend ci` for every frontend UI change; keep new captures as candidates and never advance manifest state or the enforced registry before complete technical evidence and explicit project-owner acceptance. A profile, constitution, theme, shared-owner, consumer, evidence, or acceptance change reruns the requirements named by that profile's invalidation map. Approval provenance stays in the task or pull request.

The manifest keeps lifecycle, review acceptance, and perceptual evidence separate for deterministic policy. Human review does not: lead with exactly one derived review-unit status—`In progress`, `Awaiting review`, or `Complete`—and one decision. Expose the raw states only for audit or when they disagree. This roll-up is presentation, not a fourth persisted state machine.

Choose semantic meaning before a component. Product UI uses an accessible reviewed primitive when one maps that meaning; native fallback behavior requires an accepted platform need. Selected values and options use the same display-label source, icons support rather than replace accessible names, and fixed controls retain stable geometry.

Treat component visuals as mappings of the constitution, not independent conventions. Feature code uses defaults and documented props; it does not locally alter visual treatment through style overrides, selectors, or wrapper styling. If no semantic role fits, return to `$govern-ui` before implementation instead of creating a component-local rule.

Use these ownership layers:

- **Upstream zone** — `frontend/src/components/ui` plus baseline-tracked shadcn support files contain reviewed registry source. Keep registry-default visuals and APIs; do not add product variants, business logic, feature/shared imports, or internal styling. `frontend/ui-baseline.json` records the approved snapshot and the reason/sign-off reference for each explicit exception.
- **Theme zone** — [theme/axis-theme.json](../../theme/axis-theme.json) owns reusable color, typography, spacing, density, radius, elevation, icon, motion, and layer values. Generated CSS, typed runtime roles, and their merge semantics are committed; `frontend/src/index.css` owns imports and base styles only. Authored consumers compose the generated `axisStyles` roles with standard utilities rather than copying values or spelling reusable `*-axis-*` utilities.
- **App-pattern zone** — `frontend/src/components/shared` owns reusable Axis composition and adapters. Give a surface owner narrow semantic props: leaf content stays inside owner-rendered anatomy; regions with their own states, relationships, or actions use typed models rendered by the owner.
- **Feature zone** — feature components compose defaults and app patterns. They provide product data, state, and commands; they do not pass visual or mechanical capability across a surface boundary.

Dependency flows downward through the constitution, theme, upstream primitives, app patterns, page archetypes, app shell, and feature composition. Features do not own reusable values, global timing, scroll containers, overlay mechanics, focus recovery, control visuals, or alternate page anatomies. The app shell owns stable viewport and context-transition mechanics; the active page archetype owns route geometry; the feature owns product state and composition.

[docs/playbooks/client-experience.md](./client-experience.md) owns the self-directed experience contract, view hierarchy, relationship selection, semantic component choice, and pre-JSX audit. Feature code must not import raw `Badge`.

The [Axis UI Constitution](../foundations/visual-system/axis-visual-system.md#interaction-state-model) owns the single interaction-state hierarchy and async grammar. Registry primitives and app patterns map those roles; `frontend/src/components/shared/interactionStates.ts` is only a code adapter. Compare equivalent meaning across overlays, navigation, collections, forms, and feedback; do not turn an implementation detail into durable guidance.

[docs/playbooks/client-experience.md](./client-experience.md#semantic-component-selection) owns semantic feedback, state-label, metadata, relationship, and action selection. Feature code consumes those meanings and never imports raw provider components to create another vocabulary.

Use `$govern-ui` for constitution/theme decisions, registry diffs, baseline refreshes, or provider changes. Never bulk-overwrite unrelated registry components. A reviewed upstream sync and matching baseline refresh need provenance when they introduce no customization; constitution, semantic-value, provider, primitive-exception, or cross-feature convention changes require the Design Gate and applicable sign-off.

## Styling

Use Tailwind utilities consistently. Outside the upstream and theme zones, compose reusable visual roles through the generated typed `axisStyles` contract and use standard utilities for local structure. Do not author raw `*-axis-*` utility strings, hard-coded palette utilities, arbitrary Tailwind values, component-local colors, selector-based custom CSS, or one-off visual systems. The AST consumption gate prevents raw semantic-role bypass; role validity and exact class projection remain owned by the theme schema and generator. Remove obsolete styling and component API surface when the UI path that used them is removed or replaced.

## Security

Do not store auth tokens in `localStorage`. Treat permission/visibility behavior as product behavior with tests.

## Performance

Keep heavy canvas and builder interactions scoped, virtualized, or memoized when needed. Test visible behavior first.

## Accessibility baseline

Keyboard, focus, labels, error text, and disabled/loading states must be observable and testable.
