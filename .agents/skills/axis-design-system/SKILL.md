---
name: axis-design-system
description: Route and execute Axis design-system work safely. Use when changing tokens, primitive contracts, UI primitives, design-source handoff, screen/component consumer contracts, or deterministic enforcement/docs for design-system rules.
---

# Axis Design System

## Goal

Keep Axis design-system changes grounded in the source of truth: approved design-source decisions, executable tokens, UI primitives, component contracts, and deterministic enforcement. Treat this skill as a router plus checklist; do not duplicate the detailed rules from the owner docs.

## Required Chaining

- Use `$axis-design-gate` before non-trivial design-system edits.
- Use `$axis-frontend-feature` when changing SPA routes, feature components, forms, or product screens.
- Use `$axis-visual-artifact` when touching design-source rows, committed previews, Mermaid, or visual artifact docs.
- Use `$axis-api-contract` or `$axis-cross-module-contract` first when design-system work reveals API or cross-module contract changes.
- Use `$axis-ready-review` before asking whether the branch is ready; use `$axis-pull-request` before opening or marking a PR ready.

## Owner Docs

Read only the docs needed for the touched surface:

- Always read `AGENTS.md`, `docs/playbooks/design-system.md`, and `docs/playbooks/agent-checklist.md`.
- For frontend runtime changes, read `docs/playbooks/frontend.md`.
- For design-source or preview work, read `docs/playbooks/design-source.md` and `docs/playbooks/visual-artifact-checklist.md`.
- For policy or script enforcement, read `docs/playbooks/scripts.md` and `docs/REVIEW_FINDINGS.md`.
- For product screen behavior, read the owning `docs/use-cases/**` spec before touching UI.

## Workflow

1. Classify the surface.
   - Token, primitive, contract, or verification foundation: keep the PR focused on design-system infrastructure.
   - Product screen or consumer migration: keep behavior tied to its use-case spec and compose existing primitives first.
   - Enforcement work: update the guard, negative policy tests, and review finding ledger together.
   - Catalog route, image snapshot, or visual baseline request: treat it as non-default automation and require an explicit Design Gate decision before adding it.

2. Trace the blast radius with `rg`.
   - Search token names, primitive component names, route names, guard names, and test filenames.
   - Search before claiming a component, contract, or rule is unused.

3. Preserve source-of-truth order.
   - Add or update tokens before component styles that consume them.
   - Add or update shared primitives before feature screens consume them.
   - Update `frontend/src/design-system/primitive-contracts.ts` before broad primitive use.
   - Do not add per-file allowlists for design-system violations; add the missing token, primitive, contract, or documented workaround.
   - Prefer approved source tokens and executable registries over catalog pages, screenshots, or visual baselines.

4. Check guard quality before adding enforcement.
   - Add a deterministic guard only for a reusable invariant backed by a source-of-truth file or registry.
   - Do not encode exact route names, CSS fragments, translation keys, or one-off screen incidents unless that exact value is the contract.
   - If the rule needs visual judgment or has only one known case, record it as review-only guidance in `docs/REVIEW_FINDINGS.md` instead of adding a guard.

5. Keep behavior separate from foundation.
   - Do not migrate unrelated screens in a foundation PR.
   - Do not invent product copy, ACs, endpoints, roles, or states from design-system work.
   - If a screen needs new behavior, switch to the owning use-case workflow before coding it.

6. Verify the exact surface.
   - Primitive/contract/style change: run `python scripts/axis.py check frontend-style`, `python scripts/axis.py check frontend-component-composition`, `python scripts/axis.py frontend ci`, and `python scripts/axis.py frontend test`.
   - Product screen verification change: run the affected Playwright screen spec after inspecting the rendered target.
   - Enforcement/docs change: run `python scripts/axis.py check policy-tests` and `python scripts/axis.py check doc-drift`.
   - Skill change: run `python scripts/axis.py check codex-skills` and the skill-creator `quick_validate.py` for the changed skill.
   - Before review, run `python scripts/axis.py verify`.

## Output

Report changed design-system surfaces, source-of-truth files updated, guard/test updates, verification evidence, and docs or review-ledger updates. Name any deferred follow-up with the exact owner doc or use-case section.
