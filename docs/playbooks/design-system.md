# Design System

> **Navigation**: [<- docs/README.md](../README.md) . [<- frontend](./frontend.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-design-system` for tokens, primitives, contracts, catalog, and enforcement work.

## Source Boundaries

Approved design decisions flow into executable tokens, primitive contracts, shared components, and consumer contracts. Screens do not invent design-system rules locally.

## Pixel-Perfect Definition

Match layout, spacing, typography, state, and interaction intent from the approved source while preserving accessibility and product behavior.

## Token Taxonomy

Use semantic tokens for color, spacing, radius, typography, shadows, and state. Do not encode one-off visual values in feature components.

## Token Export/Import Convention

Token generation and checks go through Axis wrappers. Generated outputs are committed only when the source changed.

## Component Inventory

`components/ui` is for shadcn primitives; Axis-authored reusable components belong in `components/shared`.

## Variant Matrix

Variants must be named, finite, testable, and backed by token/primitive contracts.

## Primitive Contracts

Every promoted primitive needs owner, source, readiness, accessibility, states, token families, and tests.

## Consumer Contracts

Route-bound product UI files must have consumer contract rows when they are part of the design-system surface.

## Enforceable Contract

Only automate reusable invariants backed by a source-of-truth file or registry. Visual judgment stays review-owned.

## Automation Boundary

Do not add screenshot/baseline automation for one case unless a reusable contract exists.

## Implementation Rules

Compose existing primitives first. Do not bypass the primitive layer with native controls in feature code.

## Component Verification Coverage

Tests should prove variants, states, keyboard/focus behavior, and contract assumptions that users can observe.

## PR Sequence

Use `$axis-design-gate`, make the focused change, run narrow checks while iterating, then use `$axis-ready-review`.
