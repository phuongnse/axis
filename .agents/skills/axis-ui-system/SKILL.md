---
name: axis-ui-system
description: Define and govern the Axis UI constitution, semantic visual values, golden references, view composition, component conformance, UI source ownership, interaction consistency, and safe replacement. Use for visual-language or page-archetype design, design-system definition or adoption, table-first resource workspaces, managed windows, workbench exceptions, visual acceptance, screen hierarchy, semantic tokens, interaction states, shadcn registry source, UI baseline, shared visual APIs, primitive exceptions, providers, or registry/provider upgrades.
---

# Axis UI System

## Goal

Make every Axis surface derive from one product-level UI constitution. Components implement semantic roles; they never become independent sources of visual or interaction policy.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- Apply the sign-off and provenance rules from [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design); do not broaden or weaken them here.
- `frontend/ui-foundation.json` is the machine-readable phase and golden reference. Keep it aligned with the `ui-design` workflow; component lists and prose status cannot advance it.
- A new or replaced cross-feature visual language or page archetype stops at one golden reference until explicit user visual acceptance freezes the contract; do not migrate consumers before that evidence.
- Do not refresh the baseline merely to silence unexplained drift or discard existing exception evidence.
- Run owned checks directly; unresolved verification command selection **Delegates** to `$axis-script-scope`.

## Inputs

- Requested UI change, current design phase, and [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design) contract.
- User task, read/edit mode, content roles, hierarchy, relationships, and responsive/accessibility expectations for a new or changed view.
- Representative routes and complete visual states across light/dark, desktop/compact, pointer, and keyboard use.
- Applicable page-archetype foundations, including [Collection Page](../../../docs/foundations/data-display/collection-page.md) and [Managed Dialog](../../../docs/foundations/overlays/managed-dialog.md).
- Registry config/source, baseline, affected consumers/tests, and provider diff.
- Design Gate and sign-off evidence when triggered.

## Workflow

1. Classify constitution definition, golden reference, accepted-contract adoption, localized composition, theme, registry sync, exception, or provider/style replacement.
2. Inventory representative surfaces before proposing code. Define product-level semantic roles across color, typography, spacing, density, radius, elevation, iconography, motion, layering, layout, responsive behavior, accessibility, feedback, and interaction state. Keep exact reusable values in `theme/axis-theme.json`; keep product-neutral behavior in the visual-system contract. Do not define policy by enumerating components.
3. Keep authenticated resource management table-first through [Collection Page](../../../docs/foundations/data-display/collection-page.md): one primary data table launches create, view, and edit work into app-scoped managed windows. Reserve alert dialogs for bounded confirmation and dedicated workbench routes for workflows that meet the foundation's complex-layout exception.
4. Changing durable visual guidance **Requires** `$axis-doc-hygiene`: update the visual-system contract and semantic theme only; link from consumers and remove superseded guidance. Before JSX, record the semantic-role, hierarchy, anatomy, state, responsive, and accessibility matrices that the golden reference must prove.
5. Consume [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md); confirm mode, relationships, vocabulary ownership, semantic component mapping, and every badge, tag, card, border, icon, and heading has one distinct job.
6. Run `python scripts/axis.py check ui-foundation`, `python scripts/axis.py check theme`, and `python scripts/axis.py check ui-baseline`; trace primitives, consumers, tests, provider leakage, semantic literals, and visual overrides with `rg`. Compare equivalent semantic roles across overlays, navigation, collections, forms, and feedback instead of comparing component names.
7. Choose one source owner per decision: the visual-system contract owns invariants, the theme owns reusable values, upstream primitives own mechanics, app patterns map roles to reusable composition, and features own product state plus outer layout. A component-specific test may prove a mapping but cannot create a new convention.
8. For a cross-feature definition, record only phase, golden archetype, and route in `frontend/ui-foundation.json`. Implement the real golden reference and the smallest shared-role mappings it needs. Prove complete data, loading, empty, error, permission, and mutation states plus light/dark, desktop/compact, pointer, and keyboard behavior; do not migrate unrelated consumers before acceptance.
9. Advance to `reference-ready` only after the running golden reference and focused browser evidence exist. Advance to `accepted` only after explicit user visual acceptance. Keep bounded migrations at `accepted`; advance to `adopted` only after all active supported surfaces conform and current evidence proves the adoption. Rework the reference at `defined` when the accepted contract changes. Neither tests nor documentation substitutes for user visual acceptance, and acceptance does not imply adoption.
10. For registry work, run the dependency-risk gate, preview and diff only the named component family through the Axis shadcn wrapper, and refresh `frontend/ui-baseline.json` only with established provenance while preserving approved exceptions.
11. Name required evidence: design phase, acceptance and adoption status, owning constitution/theme, archetype and semantic audit, representative matrices, golden-reference browser evidence, baseline, frontend quality, affected consumers, and registry diff when applicable. Run owned checks directly and use the verification handoff only when command selection remains unresolved.

## Output

Report phase, acceptance/adoption status, owner, archetype and semantic decisions, visual/state/responsive matrices, golden-reference evidence, provenance/diff, baseline, conformance evidence, and unresolved exceptions or provider leakage.
