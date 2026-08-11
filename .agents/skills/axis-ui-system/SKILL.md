---
name: axis-ui-system
description: Define and govern the Axis UI constitution, semantic visual values, enforced surface contracts, view composition, component conformance, UI source ownership, interaction consistency, and safe replacement. Use for visual-language or surface-archetype design, design-system definition or enforcement, table-first resource workspaces, account surfaces, managed windows, workbench exceptions, screen hierarchy, semantic tokens, interaction states, shadcn registry source, UI baseline, shared visual APIs, primitive exceptions, providers, or registry/provider upgrades.
---

# Axis UI System

## Goal

Make every Axis surface derive from one product-level UI constitution. Components implement semantic roles; they never become independent sources of visual or interaction policy.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- Apply the sign-off and provenance rules from [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design); do not broaden or weaken them here.
- `frontend/ui-foundation.json` is the strict lifecycle plus contract/evidence manifest for defined-or-later contracts. Only entries whose state is `enforced` and whose id appears in its checked `enforcedContracts` registry belong to the enforced-contract union; `verified` and `enforced` require version-controlled perceptual evidence. `frontend/src/lib/ui-foundation.ts` owns the finite typed consumer mapping and derives that union from the registry, while `frontend/src/lib/active-surface-registry.ts` owns real owner/implementation symbols. Keep all three aligned with the `ui-design` workflow; filename/path lists, prose, or screenshots cannot establish conformance by themselves.
- A new or replaced cross-feature visual language or surface archetype is incomplete until its owner, contract evidence, active consumers, and deterministic conformance policy land together. Do not keep a draft owner, legacy composition, or unverified consumer on the supported path.
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

1. Classify constitution definition, surface-contract definition, verified enforcement, localized composition, theme, registry sync, exception, or provider/style replacement.
2. Inventory representative surfaces before proposing code. Define product-level semantic roles across color, typography, spacing, density, radius, elevation, iconography, motion, layering, layout, responsive behavior, accessibility, feedback, and interaction state. Keep exact reusable values in `theme/axis-theme.json`; keep product-neutral behavior in the visual-system contract. Do not define policy by enumerating components, CSS properties, or DOM symptoms.
3. Keep authenticated resource management table-first through [Collection Page](../../../docs/foundations/data-display/collection-page.md): one primary data table launches create, view, and edit work into app-scoped managed windows. Reserve alert dialogs for bounded confirmation and dedicated workbench routes for workflows that meet the foundation's complex-layout exception.
4. Changing durable visual guidance **Requires** `$axis-doc-hygiene`: update the visual-system contract and semantic theme only; link from consumers and remove superseded guidance. Before JSX, record the semantic-role, hierarchy, anatomy, state, responsive, accessibility, and evidence matrices that the surface owner must enforce.
5. Consume [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md); confirm mode, relationships, vocabulary ownership, semantic component mapping, and every badge, tag, card, border, icon, and heading has one distinct job.
6. Run `python scripts/axis.py check ui-foundation`, `python scripts/axis.py check theme`, `python scripts/axis.py check ui-baseline`, and `python scripts/axis.py frontend ci`. Inspect typed catalog/real-symbol registry diffs plus rendered markers and evidence. Use `rg` only for bounded discovery or a one-time retirement sweep; never make filename or source-text patterns the durable proof of ownership. Compare equivalent semantic roles across overlays, navigation, collections, forms, and feedback instead of comparing component names.
7. Choose one source owner per decision: the visual-system contract owns invariants, the theme owns reusable values, upstream primitives own mechanics, app patterns and finite surface contracts map roles to reusable composition, and features own product state plus relationships inside declared semantic slots. Define a surface API by capability, not implementation terms: keep leaf content inside owner-rendered anatomy; represent any region with its own states, relationships, or actions as a typed semantic model rendered by the owner; never use generic JSX as subsystem anatomy. Let type and parser-backed boundaries prove that ownership, and let component/browser tests prove contract outcomes. A component-specific test may prove a mapping but cannot create a new convention.
8. For a cross-feature definition, add one finite shared contract owner at manifest state `defined`, its foundation spec, and its current component/browser evidence paths to `frontend/ui-foundation.json`; keep perceptual evidence empty until version-controlled comparisons exist. Add its id and active consumers to the typed catalog and bind real owner/implementation symbols in the registry. Require the compatible typed surface id in the owner's semantic API, keep feature/route imports out through the parser-backed module rule, and emit contract/id markers at the rendered owner boundary. Advance to `verified` only with the required perceptual evidence; advance to `enforced` and add the id to `enforcedContracts` only with complete lifecycle proof. Prove complete data, loading, empty, error, permission, and mutation states plus light/dark, desktop/compact, pointer, and keyboard behavior. Do not infer adoption from filenames, direct-import text, or matching markup.
9. Move task and manifest state from `defined` to `verified` only after focused contract-API, component, browser, accessibility, responsive, and visual evidence passes. Move to `enforced` only after typecheck proves every active registration and real-symbol mapping, rendered evidence confirms owner composition, retired compositions are removed, and deterministic policy passes. The registry is an ownership/evidence map, not a screen-design catalog: a new consumer of an unchanged enforced owner needs typed registration and product-state evidence, while a shared-owner or constitution change reopens the lifecycle for all affected consumers. Requested and authorized work stays task-local; defined-or-later contracts may be cataloged on mainline only with their honest state and current evidence. Revising an enforced contract returns to `authorized` and lands as one clean replacement with current evidence.
10. For registry work, run the dependency-risk gate, preview and diff only the named component family through the Axis shadcn wrapper, and refresh `frontend/ui-baseline.json` only with established provenance while preserving approved exceptions.
11. Name required evidence: workflow state, owning constitution/theme, contract owner, registered consumers, archetype and semantic audit, representative matrices, component/browser/visual evidence, baseline, frontend quality, retirement sweep, and registry diff when applicable. Run owned checks directly and use the verification handoff only when command selection remains unresolved.

## Output

Report workflow state, contract owner, registered consumers, archetype and semantic decisions, visual/state/responsive matrices, component/browser/visual evidence, provenance/diff, baseline, conformance evidence, retirement sweep, and unresolved exceptions or provider leakage.
