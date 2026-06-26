---
name: axis-design-system
description: Route and execute Axis design-system work safely. Use when changing Open Design publish/sync workflow, design-source handoff, generated tokens, primitive contracts, UI primitives, screen/component consumer contracts, or deterministic enforcement/docs for design-system rules.
---

# Axis Design System

## Goal

Keep Axis design-system changes grounded in the source of truth: approved design-source decisions, executable tokens, UI primitives, component contracts, and deterministic enforcement. Treat this skill as a router plus checklist; do not duplicate the detailed rules from the owner docs.

## Inputs

- Touched design-system surface: token, primitive, component contract, consumer screen, visual artifact, or guard.
- Owning design source, use-case spec, or registry file for that surface.
- Existing consumers, tests, guards, and docs found through `rg`.

## Source Layout

- Treat `design-sources/open-design/` as the Open Design source root.
- Put shared design-system source files directly under `design-sources/open-design/` unless a use-case subfolder is needed.
- Use subfolders under `design-sources/open-design/` only for domain/use-case-owned design sources.
- Treat `frontend/src/design-system/` as generated/runtime design-system output for the SPA.
- Do not edit generated frontend design-system output by hand when it can be derived from the Open Design source.

## Required Chaining

- Use `$axis-design-gate` before non-trivial design-system edits.
- Use `$axis-frontend-feature` when changing SPA routes, feature components, forms, or product screens.
- Use `$axis-visual-artifact` when touching design-source rows, committed previews, Mermaid, or visual artifact docs.
- Use `$axis-api-contract` first when design-system work reveals API contract changes.
- Use `$axis-ready-review` before asking whether the branch is ready; use `$axis-pull-request` before opening or marking a PR ready.

## Owner Docs

Read only the docs needed for the touched surface:

- Always read [AGENTS.md](../../../AGENTS.md), [docs/playbooks/design-system.md](../../../docs/playbooks/design-system.md), and [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md).
- For frontend runtime changes, read [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md).
- For design-source, preview, or Mermaid work, use `$axis-visual-artifact`.
- For policy or script enforcement, read [docs/playbooks/scripts.md](../../../docs/playbooks/scripts.md) and [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).
- For product screen behavior, read the owning spec under [docs/use-cases/README.md](../../../docs/use-cases/README.md) before touching UI.

## Workflow

1. Classify the surface.
   - Token, primitive, contract, or verification foundation: keep the PR focused on design-system infrastructure.
   - Product screen or consumer migration: keep behavior tied to its use-case spec and compose existing primitives first.
   - Enforcement work: update the guard, negative policy tests, and enforcement ledger together.
   - Catalog route, image snapshot, or visual baseline request: treat it as non-default automation and require an explicit Design Gate decision before adding it.

2. Trace the blast radius with `rg`.
   - Search token names, primitive component names, route names, guard names, and test filenames.
   - Search before claiming a component, contract, or rule is unused.

3. Preserve source-of-truth order.
   - Publish or update the Open Design source under `design-sources/open-design/` before changing generated frontend output.
   - Sync generated outputs into `frontend/src/design-system/` before component styles consume them.
   - Add or update tokens before component styles that consume them.
   - Add or update shared components before feature screens consume them.
   - Update `frontend/src/design-system/primitive-contracts.ts` before broad primitive use.
   - Add or update drift checks when a generated output can get out of sync with its source.
   - If shadcn provides the primitive, install or copy the shadcn implementation into `frontend/src/components/ui`, keep its standard API/classes, and migrate consumers to that API.
   - Keep `components/ui` shadcn-only with registry kebab-case filenames; put Axis-authored shared components in `components/shared` with `PascalCase.tsx`.
   - Keep component-owned invariants stronger than caller customization; do not let broad passthrough props create contradictory component states.
   - Do not add ad hoc per-file allowlists for design-system violations; add the missing token, primitive, contract, source provenance, or shared component.
   - Prefer approved source tokens and executable registries over catalog pages, screenshots, or visual baselines.

4. Handle Open Design publish/sync as a generated-artifact workflow.
   - Keep Open Design as the authoring source of truth.
   - Keep generated SPA files committed only as deterministic outputs needed by build, tests, and review.
   - Use an Axis wrapper for sync or generation when one exists; add or update the wrapper before documenting raw commands.
   - A publish/sync task is incomplete until the source package, generated output, and drift check agree.
   - Product screens consume the generated tokens/primitives/contracts; they do not read Open Design at runtime.

5. Check guard quality before adding enforcement.
   - Add a deterministic guard only for a reusable invariant backed by a source-of-truth file or registry.
   - Do not encode exact route names, CSS fragments, translation keys, or one-off screen incidents unless that exact value is the contract.
   - If the rule needs visual judgment or has only one known case, record it as review-only guidance in [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md) instead of adding a guard.

6. Keep behavior separate from foundation.
   - Do not migrate unrelated screens in a foundation PR.
   - Do not invent product copy, ACs, endpoints, or states from design-system work.
   - If a screen needs new behavior, switch to the owning use-case workflow before coding it.

7. Verify the exact surface.
   - Open Design publish/sync change: run the sync command, drift check, and the focused docs/link checks for touched source rows.
   - Primitive/contract/style change: run the focused frontend style/composition check plus the smallest frontend test that proves the change.
   - Product screen verification change: run the affected browser or component spec after inspecting the rendered target.
   - Enforcement/docs change: run the focused policy/docs check for the touched rule.
   - Skill change: when available, run skill-creator `scripts/quick_validate.py <skill-folder>`, then `python scripts/axis.py check codex-skills`.
   - Before review, use `$axis-ready-review`.

## Output

Report changed design-system surfaces, source-of-truth files updated, guard/test updates, verification evidence, and docs or review-ledger updates. Name any deferred follow-up with the exact owner doc or use-case section.
