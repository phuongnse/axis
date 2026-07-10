# Frontend Playbook

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use `$axis-frontend-feature` for SPA feature work. Use `$axis-frontend-foundation` for app shell or shared SPA foundation work owned by [docs/foundations/README.md](../foundations/README.md).

## UX-first product UI

Build the workflow, not a landing page or explanation page. Visible copy should help users act and avoid internal architecture terms.

On localized surfaces, user-facing product copy must use the frontend translation layer instead of component-local static text. Keep non-product constants, routes, and protocol values separate from visible copy.

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

## TypeScript discipline

Strict TS, no `any`, generated API types for backend contracts.

## Server-owned values

Frontend forms submit user-authored decisions and required protocol tokens only. If a value is derived by the system, the UI may show an advisory read-only preview, but the generated API request type must exclude that value and the response remains the source of truth.

## Routing

Use TanStack Router patterns already in the app. Classify every route as authenticated, guest-only, or public-neutral before adding it. Protected routes live under the authenticated layout; guest-only auth or registration routes live under the guest-only layout; public-neutral routes stay outside both access groups.

## Component design

Use the approved design system first. For Axis today, shared UI primitives come from shadcn-owned `frontend/src/components/ui` contracts unless the Design Gate records an explicit exception. Product UI uses the interaction-consistent shadcn primitive when one exists; native fallback variants are exception-only and require a documented platform-native behavior need plus explicit sign-off. Select triggers and options use the same user-facing display-label source; never expose raw protocol values as the selected label. If the current component contract lacks a needed capability, propose the smallest design-system addition or component API change and wait for explicit user sign-off before adding it. Custom components or bespoke interaction behavior are exceptions only: document why the existing contract does not fit and get explicit sign-off before implementation. Use icons for iconable actions, labels/tooltips for clarity, and stable dimensions for fixed controls.

Treat design-system component visuals as owned by the component contract. Feature code uses defaults and documented props; it does not locally alter component visual treatment through style overrides, selectors, or wrapper styling. If the requested UI needs a visual deviation or component API change, stop before implementation, name the deviation, and get explicit user sign-off; this applies even when the broader change is standard-tier.

## Styling

Use Tailwind utilities consistently. Avoid one-off local visual systems. Remove obsolete styling and component API surface when the UI path that used them is removed or replaced.

## Security

Do not store auth tokens in `localStorage`. Treat permission/visibility behavior as product behavior with tests.

## Performance

Keep heavy canvas and builder interactions scoped, virtualized, or memoized when needed. Test visible behavior first.

## Accessibility baseline

Keyboard, focus, labels, error text, and disabled/loading states must be observable and testable.
