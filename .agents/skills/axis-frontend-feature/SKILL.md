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
- Use generated API types — do not hand-write duplicate wire shapes.
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
   - Keep feature imports through the existing feature `index.ts` pattern.

4. Implement the UI behavior.
   - Use generated API types instead of hand-written duplicate shapes.
   - Use TanStack Query for server state and Zustand only for client-only state.
   - Use React Hook Form plus Zod for forms.
   - Include loading, empty, error, validation, disabled, and success states when the workflow needs them.
   - Keep screen shape tied to owning use-case flows, ACs, and implementation-status gaps.
   - Do not store auth tokens in `localStorage`.
   - Keep visible text focused on the product workflow, not developer instructions.

5. Test behavior.
   - Use Vitest and Testing Library.
   - Prefer `userEvent`.
   - Assert observable UI behavior and API interaction, not component internals.
   - Cover validation, empty/error states, and permission/visibility behavior when in scope.

6. Verify.
   - During development, run the smallest targeted frontend test that proves changed behavior; use type/lint only for static edits.
   - For visual or layout-sensitive changes, inspect the route at desktop and mobile sizes with Playwright or an available browser-capable tool when the app is runnable.
   - For browser-level journey evidence that needs the local stack, use `python scripts/axis.py local-dev e2e`.
   - Ready review: `$axis-ready-review`.

## Output

Report the feature paths changed, generated API dependency status, frontend tests, visual verification, and docs/status updates or why they were not triggered.
