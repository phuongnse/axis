# Design System

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/frontend.md](./frontend.md) · [AGENTS.md](../../AGENTS.md)

Use `$axis-design-system` for tokens, primitives, contracts, catalog, and enforcement work.

## Rules

- Approved design decisions flow into executable tokens, primitive contracts, shared components, and consumer contracts.
- Screens do not invent design-system rules locally.
- Match layout, spacing, typography, state, and interaction intent from the approved source while preserving accessibility and product behavior.
- Use semantic tokens for color, spacing, radius, typography, shadows, and state.
- Do not encode one-off visual values in feature components.
- Token generation and checks go through Axis wrappers; generated outputs are committed only when the source changed.
- `components/ui` is for shadcn primitives; Axis-authored reusable components belong in `components/shared`.
- Compose existing primitives first.

## Contracts

- Variants must be named, finite, testable, and backed by token/primitive contracts.
- Every promoted primitive needs owner, source, readiness, accessibility, states, token families, and tests.
- Route-bound product UI files need consumer contract rows when they are part of the design-system surface.
- Tests should prove variants, states, keyboard/focus behavior, and contract assumptions that users can observe.

## Enforcement

Only automate reusable invariants backed by a source-of-truth file or registry. Visual judgment stays review-owned.

Do not add screenshot/baseline automation for one case unless a reusable contract exists.
