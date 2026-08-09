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
- Product-neutral routing, provider, shell-state, scroll ownership, and context-transition mechanics stay here. Visible hierarchy, page archetypes, interaction treatment, semantic shared components, and draft/frozen UI state **Delegates** directly to `$axis-ui-system`; product journeys **Delegate** to `$axis-frontend-feature`.

## Inputs

- Foundation surface, consumers, guarantees, and out-of-scope product behavior.
- Existing foundation docs, routes/components, and evidence sidecar.
- Current prerequisite decisions supplied by the caller.

## Workflow

1. Locate the owner under [docs/foundations/README.md](../../../docs/foundations/README.md); do not create placeholders or an unapproved new surface.
2. Read [docs/playbooks/docs-style.md](../../../docs/playbooks/docs-style.md), related foundation code/tests, and dependent use cases only to preserve boundaries.
3. Define purpose, consumers, activation, guarantees, alternate/error behavior, cohesive ACs, Acceptance Test Matrix, out of scope, implementation status, and decisions. Keep product outcomes in their use cases and implementation libraries or provider mechanics in their stack/UI owner.
4. Name consuming surfaces and the reusable accessibility, responsiveness, localization, navigation, and interaction guarantees. Describe generic extension points rather than embedding one consumer's identifiers, lifecycle, DTOs, or copy.
5. Implement product-neutral mechanics here with focused foundation tests. Delegate only product behavior to `$axis-frontend-feature`; delegate any visible cross-feature convention or shared visual API directly to `$axis-ui-system` and consume the returned phase decision.
6. When the foundation participates in the golden reference, keep its exact feature support roots in `frontend/ui-foundation.json`; do not use that exception to migrate unrelated consumers.
7. Reconcile status with required AT rows and the sibling evidence sidecar; temporary smoke or screenshots do not replace committed acceptance evidence.
8. Run `python scripts/axis.py check ui-foundation` when frontend UI state is involved, then `python scripts/axis.py check foundation-docs`, and return the updated contract/evidence to the caller.

## Output

Report foundation owner, guarantees/consumers, decisions, delegated slices, evidence status, checks, and open product handoffs.
