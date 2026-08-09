---
name: axis-ui-system
description: Define and govern the Axis visual system, golden references, view composition, semantic components, UI source ownership, interaction consistency, and safe replacement. Use for visual-language or page-archetype design, design-system definition or freeze, table-first resource workspaces, managed windows, workbench exceptions, visual acceptance, screen hierarchy, semantic tokens, interaction states, shadcn registry source, UI baseline, shared visual APIs, primitive exceptions, providers, or registry/provider upgrades.
---

# Axis UI System

## Goal

Make Axis visual language, page archetypes, view hierarchy, and semantic components predictable from one accepted reference while keeping registry source replaceable and customization explicit, app-owned, tested, and reviewable.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- Apply the sign-off and provenance rules from [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design); do not broaden or weaken them here.
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

1. Classify localized composition, cross-feature visual-system definition, golden reference, frozen-contract migration, theme, registry sync, exception, or provider/style replacement.
2. For a visual-system definition, inventory representative current surfaces before proposing code. Define one coherent foundation across color, typography, spacing and density, radius, elevation, iconography, motion, layout, responsive behavior, accessibility, feedback, and interaction states. Map each route to an approved archetype instead of forcing unrelated workflows into one layout.
3. Keep authenticated resource management table-first through [Collection Page](../../../docs/foundations/data-display/collection-page.md): one primary data table launches create, view, and edit work into app-scoped managed windows. Reserve alert dialogs for bounded confirmation and dedicated workbench routes for workflows that meet the foundation's complex-layout exception.
4. Changing durable visual guidance **Requires** `$axis-doc-hygiene`: update one owning contract and link from consumers; do not create a parallel design-system spec, migration log, or approval record. Before JSX, record the exact hierarchy, anatomy, component, state, responsive, and accessibility matrices that the golden reference must prove.
5. Consume [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md); confirm mode, relationships, vocabulary ownership, semantic component mapping, and every badge, tag, card, border, icon, and heading has one distinct job.
6. Run `python scripts/axis.py check ui-baseline`; trace primitives, consumers, tests, provider leakage, raw semantic components, and visual overrides with `rg`. For state changes, compare transient, persistent, focus, disabled, and destructive roles across the representative matrix.
7. Choose one source owner: upstream registry, semantic theme, app-owned shared pattern, or feature layout. State visuals outside registry primitives belong only to `frontend/src/components/shared/interactionStates.ts`.
8. For a cross-feature definition, implement only one real golden reference and its required shared owners. Prove complete data, loading, empty, error, permission, and mutation states plus light/dark, desktop/compact, pointer, and keyboard behavior; do not start the consumer migration in the same unaccepted phase.
9. Obtain explicit user visual acceptance of the running golden reference, then freeze its current contract. Rework the reference when acceptance changes; after freeze, `$axis-frontend-feature` owns bounded consumer migrations and returns visual deviations here.
10. For registry work, run the dependency-risk gate, preview and diff only the named component family through the Axis shadcn wrapper, and refresh `frontend/ui-baseline.json` only with established provenance while preserving approved exceptions.
11. Name required evidence: design phase and freeze status, owning visual contract, archetype and semantic audit, representative matrices, golden-reference browser evidence when triggered, baseline, frontend quality, affected shared consumers, and registry diff when applicable. Run owned checks directly and use the verification handoff only when command selection remains unresolved.

## Output

Report phase, freeze status, owner, archetype and composition decisions, visual/state/responsive matrices, golden-reference acceptance evidence when triggered, provenance/diff, baseline, contract/consumer evidence, and unresolved exceptions or provider leakage.
