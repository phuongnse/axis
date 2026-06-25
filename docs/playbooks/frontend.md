# Frontend Playbook

> **Navigation**: [<- docs/README.md](../README.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-frontend-feature` for SPA feature work and `$axis-design-system` for primitives/tokens.

## UX-first product UI

Build the workflow, not a landing page or explanation page. Visible copy should help users act and avoid internal architecture terms.

## Mobile-first layout and radius

Design from small screens up. Keep cards and controls at restrained radius unless the design system says otherwise.

## Feature folder anatomy

Feature code lives under `frontend/src/features/{name}/` and exports through the feature index. Avoid cross-feature deep imports.

## State management

Use TanStack Query for server state. Use local React state or Zustand only for client-only state.

## Localization and theme preferences

Use the app i18n/theme paths. Do not hard-code user-visible text when the surrounding feature is localized.

## TanStack Query patterns

Use stable query keys, generated API types, and explicit invalidation after mutations.

## TypeScript discipline

Strict TS, no `any`, generated API types for backend contracts.

## Routing

Use TanStack Router patterns already in the app. Protected routes live under the authenticated layout.

## Component design

Use shadcn/Axis primitives, icons for iconable actions, labels/tooltips for clarity, and stable dimensions for fixed controls.

## Styling

Use design tokens and semantic utilities. Avoid arbitrary colors/gradients, raw shadows, and one-off local visual systems.

## Security

Do not store auth tokens in `localStorage`. Treat permission/visibility behavior as product behavior with tests.

## Performance — canvas and builder UIs

Keep heavy builder interactions scoped, virtualized, or memoized when needed. Test visible behavior first.

## Accessibility baseline

Keyboard, focus, labels, error text, and disabled/loading states must be observable and testable.
