---
name: axis-frontend-foundation
description: Define or update product-neutral Axis SPA foundation contracts. Use for app frame, authenticated layout, navigation, route frames, collection infrastructure, or reusable cross-route behavior that enables use cases without owning a user journey.
---

# Axis Frontend Foundation

## Goal

Own a product-neutral foundation contract and its evidence without duplicating use-case, feature, or UI-system workflows.

## Hard gates

Follow [reference.md](../reference.md).
- Actor goals, business side effects, and product validation flows belong to `$axis-use-case-spec` or its implementation caller.
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- Source implementation **Delegates** to `$axis-frontend-feature`; UI owner changes delegate from there to `$axis-ui-system`.

## Inputs

- Foundation surface, consumers, guarantees, and out-of-scope product behavior.
- Existing foundation docs, routes/components, and evidence sidecar.
- Current prerequisite decisions supplied by the caller.

## Workflow

1. Locate the owner under [docs/foundations/README.md](../../../docs/foundations/README.md); do not create placeholders or an unapproved new surface.
2. Read [docs/playbooks/docs-style.md](../../../docs/playbooks/docs-style.md), related foundation code/tests, and dependent use cases only to preserve boundaries.
3. Define purpose, consumers, activation, guarantees/main flow, alternate/error behavior, ACs, Acceptance Test Matrix, out of scope, implementation status, and decisions. Keep product outcomes in their use cases.
4. Name consuming routes/components and the reusable accessibility, responsiveness, localization, navigation, and interaction guarantees.
5. This workflow **Delegates** implementation slices to `$axis-frontend-feature` with this contract and current Design Gate evidence.
6. Reconcile status with required AT rows and the sibling evidence sidecar; temporary smoke or screenshots do not replace committed acceptance evidence.
7. Run `python scripts/axis.py check foundation-docs` and return the updated contract/evidence to the caller.

## Output

Report foundation owner, guarantees/consumers, decisions, delegated slices, evidence status, checks, and open product handoffs.
