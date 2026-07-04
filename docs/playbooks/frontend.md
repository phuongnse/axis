# Frontend Playbook

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use `$axis-frontend-feature` for SPA feature work.

## UX-first product UI

Build the workflow, not a landing page or explanation page. Visible copy should help users act and avoid internal architecture terms.

On localized surfaces, user-facing product copy must use the frontend translation layer instead of component-local static text. Keep non-product constants, routes, and protocol values separate from visible copy.

Every route must expose an obvious next navigation path. Auth and public standalone screens declare route-level escape targets with `routeNavigation = publicRouteNavigation(...)` and render a visible sign-in, registration, back, or home-style link in every loading, success, and error state. Authenticated screens satisfy this through the app shell navigation and sign-out.

## Mobile-first layout and radius

Design from small screens up. Keep cards and controls at restrained radius unless the owning use case says otherwise.

For localized surfaces, validate copy fit in the supported languages named by the owning use case at mobile and desktop sizes. Prefer responsive layout capacity and design-system improvements over language-specific copy or styling hacks.

## Feature folder anatomy

Feature code lives under `frontend/src/features/{name}/` and exports through the feature index. Avoid cross-feature deep imports.

## State management

Use TanStack Query for server state. Use local React state or Zustand only for client-only state.

## TanStack Query patterns

Use stable query keys, generated API types, and explicit invalidation after mutations.

## TypeScript discipline

Strict TS, no `any`, generated API types for backend contracts.

## Routing

Use TanStack Router patterns already in the app. Protected routes live under the authenticated layout.

## Component design

Use the approved design system first. If the current component contract lacks a needed capability, propose the smallest design-system addition or component API change and wait for explicit user sign-off before adding it. Custom components or bespoke interaction behavior are exceptions only: document why the existing contract does not fit and get explicit sign-off before implementation. Use icons for iconable actions, labels/tooltips for clarity, and stable dimensions for fixed controls.

Treat design-system component visuals as owned by the component contract. Feature code uses defaults and documented props; it does not locally alter component visual treatment through style overrides, selectors, or wrapper styling. If the requested UI needs a visual deviation or component API change, stop before implementation, name the deviation, and get explicit user sign-off; this applies even when the broader change is standard-tier.

## Styling

Use Tailwind utilities consistently. Avoid one-off local visual systems. Remove obsolete styling and component API surface when the UI path that used them is removed or replaced.

## Security

Do not store auth tokens in `localStorage`. Treat permission/visibility behavior as product behavior with tests.

## Performance

Keep heavy canvas and builder interactions scoped, virtualized, or memoized when needed. Test visible behavior first.

## Accessibility baseline

Keyboard, focus, labels, error text, and disabled/loading states must be observable and testable.
