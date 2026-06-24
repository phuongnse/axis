---
name: axis-frontend-feature
description: Build Axis frontend feature slices safely. Use when changing SPA routes, feature folders, components, forms, data fetching, generated API-type consumers, loading/empty/error states, or UI behavior tied to a use case.
---

# Axis Frontend Feature

## Goal

Implement an Axis frontend slice with generated API types, user-visible states, accessible interactions, and tests that prove behavior.

## Workflow

1. Classify the surface.
   - Use `$axis-use-case-implementation` when the work implements a documented use case.
   - Use `$axis-api-contract` first when the frontend change needs an API shape change.
   - Use `$axis-design-gate` for non-trivial frontend behavior.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/playbooks/frontend.md`
   - `docs/playbooks/design-source.md`
   - `docs/playbooks/testing.md`
   - `docs/playbooks/agent-checklist.md`
   - The owning use-case and design-source docs when behavior or screen shape changes

3. Trace the existing feature.
   - Search routes, feature folder exports, generated API types, hooks, test files, and sibling components with `rg`.
   - Keep feature imports through the existing feature `index.ts` pattern.

4. Implement the UI behavior.
   - Use generated API types instead of hand-written duplicate shapes.
   - Use TanStack Query for server state and Zustand only for client-only state.
   - Use React Hook Form plus Zod for forms.
   - Include loading, empty, error, validation, disabled, and success states when the workflow needs them.
   - Match the owning design source/preview when screen shape changes; update the use-case `## Design Sources` row through `$axis-visual-artifact`.
   - Do not store auth tokens in `localStorage`.
   - Keep visible text focused on the product workflow, not developer instructions.

5. Test behavior.
   - Use Vitest and Testing Library.
   - Prefer `userEvent`.
   - Assert observable UI behavior and API interaction, not component internals.
   - Cover validation, empty/error states, and permission/visibility behavior when in scope.

6. Verify.
   - Run `python scripts/axis.py frontend ci`.
   - Run `python scripts/axis.py frontend test`.
   - For visual or layout-sensitive changes, use the browser skill to inspect the route at desktop and mobile sizes when the app is runnable.
   - Ready review: `$axis-ready-review`.

## Output

Report the feature paths changed, generated API dependency status, frontend tests, visual verification, and docs/status updates or why they were not triggered.
