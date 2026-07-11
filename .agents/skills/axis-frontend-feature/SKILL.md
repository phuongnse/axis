---
name: axis-frontend-feature
description: Build Axis frontend feature slices safely. Use when changing SPA routes, feature folders, components, forms, data fetching, generated API-type consumers, loading/empty/error states, or UI behavior tied to a use case.
---

# Axis Frontend Feature

## Goal

Implement an Axis frontend slice with generated API types, user-visible states, accessible interactions, and tests that prove behavior.

## Hard gates

Follow [reference.md](../reference.md).
- Use `$axis-design-gate` for non-trivial behavior; stop for high-risk sign-off before code.
- Stop for explicit user sign-off before any feature-level visual deviation from the design-system component contract or any component API change per [docs/playbooks/frontend.md#component-design](../../../docs/playbooks/frontend.md#component-design).
- Before and after edits, complete a **visual override audit** for every touched `@/components/ui` call site. `className` may carry outer layout-only concerns; internal size/spacing, typography, radius, color, border, background, shadow, and state styling require an existing prop or explicit sign-off for a shared variant/API change.
- Stop for explicit user sign-off before adding a feature-local reusable UI primitive, bypassing `frontend/src/components/ui`, or implementing bespoke interaction behavior when an approved shadcn component may cover the need.
- Treat native fallback variants as exceptions even when they exist in the shadcn registry. Use the interaction-consistent shadcn primitive unless the dossier records a platform-native behavior requirement and the user signs off.
- Select triggers and options must resolve from the same localized display-label source; do not render raw protocol values through a bare `SelectValue`.
- Use generated API types — do not hand-write duplicate wire shapes.
- Do not submit server-owned derived values from the UI; show advisory read-only previews only when the generated request contract excludes the field.
- For route-owned server data, follow [docs/playbooks/frontend.md#tanstack-query-patterns](../../../docs/playbooks/frontend.md#tanstack-query-patterns); do not leave initial route data as component-only fetch unless the dossier records why loader/prefetch is not appropriate.
- Do not claim review-ready without `$axis-ready-review`.

## Inputs

- Owning use-case and affected route or feature folder.
- Generated API type or API contract dependency, if any.
- Existing components, hooks, tests, and user-visible states found through `rg`.

## Workflow

1. Classify the surface.
   - Use `$axis-use-case-implementation` when the work implements a documented use case.
   - Use `$axis-api-contract` first when the frontend change needs an API shape change.
   - Use `$axis-design-gate` for non-trivial frontend behavior.

2. Read the owning rules.
   - [AGENTS.md](../../../AGENTS.md)
   - [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md)
   - [docs/playbooks/testing.md](../../../docs/playbooks/testing.md)
   - [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md)
   - The owning use-case docs when behavior or screen shape changes

3. Trace the existing feature.
   - Search routes, feature folder exports, generated API types, hooks, test files, and sibling components with `rg`.
   - Classify every affected route as authenticated, guest-only, or public-neutral before editing route files.
   - Keep feature imports through the existing feature `index.ts` pattern.
   - Trace the server-state path: route loader, query keys/options, API wrapper, component `useQuery`, mutation cache updates, invalidation, URL search state, and tests.
   - Inventory touched design-system call sites. Record the existing prop/variant for each visual requirement and separate layout-only classes from component visual overrides before editing.
   - Inventory the installed `frontend/src/components/ui` primitives for every interactive element in scope. Record the selected shadcn primitive or the signed-off exception before editing.

4. Implement the UI behavior.
   - Use generated API types instead of hand-written duplicate shapes.
   - Submit user-authored decisions and required protocol tokens only; when the workflow displays a server-owned derived value, render it as non-authoritative preview/read-only state and reconcile with the response value after mutation.
   - Use TanStack Query for server state and Zustand only for client-only state.
   - For route-owned server data, define feature-level query option factories with stable keys and generated response types. Use the same factories from route loaders, prefetch calls, and component `useQuery` calls.
   - Load first-paint business data through the route file form that supports loaders and call `context.queryClient.ensureQueryData(...)` for required data. Use `prefetchQuery` only for intent-based warming where rendering should not wait.
   - Prefetch detail, adjacent page, or navigation data only on clear user intent and only within auth, acceptance criteria, sensitivity, and request-cost boundaries.
   - After mutations, write returned entities into exact detail cache entries and explicitly invalidate or update affected list keys. Avoid broad feature-prefix invalidation unless the dossier names why every projection must be considered stale.
   - Put shareable pagination, filters, and selected record identifiers in route search params; keep draft-only editor state local.
   - Use React Hook Form plus Zod for forms.
   - Include loading, empty, error, validation, disabled, and success states when the workflow needs them.
   - Keep screen shape tied to owning use-case flows, ACs, and implementation-status gaps.
   - Use route access groups for route policy: authenticated pages live under `_authenticated`, guest-only auth or registration pages live under `_guest`, and public-neutral pages stay outside both groups. Put `beforeLoad` guards on the pathless access group, not on individual leaf routes, unless the owning use case defines a one-off route policy.
   - Do not create dead-end screens. Public/auth route files that render a screen state must declare `routeNavigation = publicRouteNavigation(...)`, and every public/auth route state must render a visible sign-in, registration, back, or home-style link. Redirect-only route entries that render no screen state are exempt. Authenticated pages rely on app shell navigation and sign-out.
   - Keep technical handoffs from flashing transient success screens. Successful callback, bootstrap, and redirect handoffs should complete in route loaders, guards, or silent session flows before a standalone screen renders; render a handoff screen only for deliberately held confirmations or recoverable user-action states.
   - Do not store auth tokens in `localStorage`.
   - On localized surfaces, route visible product copy through the frontend translation layer rather than component-local static text.
   - Keep visible text focused on the product workflow, not developer instructions.
   - Use shadcn-owned design-system component defaults and documented props from `frontend/src/components/ui`; if the UI requires a custom primitive, bespoke interaction behavior, visual deviation, or component API change, stop for the sign-off required by [docs/playbooks/frontend.md#component-design](../../../docs/playbooks/frontend.md#component-design).
   - Prefer interaction-consistent primitives over native fallback variants. Do not import a native fallback into product code without the documented exception required by the component-design contract.
   - Format every selected value through `SelectValue` children from the same label source as its `SelectItem`; test the initial and changed trigger labels when they differ from protocol values.
   - Keep shared primitive call sites free of local visual utilities. After sign-off, put the treatment in the owning primitive as a named shared variant and select it through props from the feature.

5. Test behavior.
   - Use Vitest and Testing Library.
   - Prefer `userEvent`.
   - Assert observable UI behavior and API interaction, not component internals.
   - When loader or prefetch behavior changes, test the observable result and the API/cache interaction that proves initial data, intent prefetch, and mutation cache updates.
   - Cover validation, empty/error states, and permission/visibility behavior when in scope.
   - Cover escape navigation for public/auth route states when the screen can be reached directly or after a failed flow. Route metadata is a contract declaration, not a substitute for the visible behavior test.
   - When adding a shared variant, test both its component contract and the feature call site's selected variant.

6. Verify.
   - During development, run the smallest targeted frontend test that proves changed behavior; use type/lint only for static edits.
   - For visual, layout-sensitive, or localized-copy changes, inspect the route in the supported languages named by the owning use case at desktop and mobile sizes with Playwright or an available browser-capable tool when the app is runnable.
   - Use `python scripts/axis.py local-dev smoke -- <playwright-args>` for fast host-browser layout/UI smoke against an already-running local stack.
   - Repeat the visual override audit on touched call sites; an unresolved local visual override fails verification.
   - For Compose-backed browser journey or acceptance evidence that needs the local stack, use `python scripts/axis.py local-dev e2e`.
   - Ready review: `$axis-ready-review`.

## Output

Report the feature paths changed, generated API dependency status, frontend tests, visual verification, and docs/status updates or why they were not triggered.
Report the design-system audit: primitives inspected, layout-only classes retained, shared variants added with sign-off, and unresolved overrides.
Include route data-loading decisions when loader, prefetch, cache update, invalidation, or URL search state changed.
