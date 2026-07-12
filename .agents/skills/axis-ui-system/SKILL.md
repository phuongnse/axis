---
name: axis-ui-system
description: Govern the Axis UI ownership boundary and safe evolution. Use when adding or changing semantic theme tokens, shared UI patterns, shadcn components, components.json, UI baseline files, component providers, visual/API exceptions, or when upgrading, replacing, or comparing shadcn registry components.
---

# Axis UI System

## Goal

Keep shadcn source replaceable while Axis customization stays explicit, app-owned, tested, and protected by sign-off.

## Hard gates

Follow [reference.md](../reference.md).
- Use `$axis-design-gate` before non-trivial changes and `$axis-ready-review` before review.
- Treat [frontend/src/components/ui](../../../frontend/src/components/ui) and baseline-tracked shadcn support files as the registry-owned upstream zone. Do not hand-edit them for product styling, business behavior, or app-specific variants.
- Stop for explicit user sign-off before changing semantic tokens, introducing a cross-feature visual/API convention, editing a registry primitive, refreshing the approved UI baseline, or changing the shadcn style, base, provider, or major library.
- Put approved app customization in [frontend/src/components/shared](../../../frontend/src/components/shared) with a narrow Axis-owned contract; do not leak provider props, types, selectors, or DOM assumptions.
- Do not refresh [frontend/ui-baseline.json](../../../frontend/ui-baseline.json) merely to silence drift. First prove the source of every changed baseline file and record the applicable sign-off.
- Record every approved upstream-zone exception in the baseline with a durable reason and sign-off reference; empty exception metadata fails policy.

## Inputs

- Requested visual, behavior, token, component, registry update, or provider change.
- Current [frontend/components.json](../../../frontend/components.json), UI baseline, affected consumers, tests, and registry diff.
- User sign-off when a hard gate requires it.

## Workflow

1. Classify the change.
   - Consumption: existing default primitive/prop; no UI-system customization.
   - App pattern: reusable Axis composition or adapter in `components/shared`.
   - Theme: semantic token value or new background/foreground pair in `src/index.css`.
   - Registry sync: upstream source changes under `components/ui`.
   - Provider/style replacement: high-risk; full Design Gate and sign-off before code.

2. Audit before editing.
   - Read [docs/playbooks/frontend.md#component-design](../../../docs/playbooks/frontend.md#component-design).
   - Run `python scripts/axis.py check ui-baseline`.
   - Search affected primitive imports, shared consumers, feature callers, tests, and visual overrides with `rg`.
   - For registry work, preview only the named component family:
     `python scripts/axis.py frontend script shadcn -- add <component> --dry-run`.
   - Inspect each changed file:
     `python scripts/axis.py frontend script shadcn -- add <component> --diff <path>`.

3. Choose the owner.
   - Keep unmodified shadcn implementation and generated support files in the upstream zone.
   - Keep token definitions in `src/index.css`; consumers use semantic utilities only.
   - Put repeated product semantics in `components/shared`, expressed as Axis concepts such as `status`, not vendor variants.
   - Keep one-off feature composition in the owning feature; `className` is outer layout-only.
   - If none fits, stop and present the smallest exception for sign-off.

4. Implement after required sign-off.
   - Registry sync: apply only reviewed components through the Axis shadcn wrapper; never bulk-overwrite unrelated primitives.
   - App pattern: compose default primitives, keep props narrow, preserve accessibility, and avoid provider-specific exports.
   - Theme: use semantic background/foreground pairs; no hard-coded palette utilities, arbitrary Tailwind values, or component-local colors.
   - Provider replacement: preserve local primitive exports where practical, migrate shared patterns before features, and keep temporary compatibility only when explicitly approved.
   - Refresh the baseline only after the reviewed result is final:
     `python scripts/axis.py frontend ui-baseline --write`.
   - For an approved primitive exception, add its `reason` and `signOff` entry to the preserved baseline exception map.

5. Verify.
   - Run `python scripts/axis.py check ui-baseline`, `python scripts/axis.py check frontend-quality`, and `python scripts/axis.py check repo-skills` when routing changed.
   - Test app-owned shared contracts and affected consumers; run focused browser evidence for visual or interaction changes.
   - Repeat the registry diff after a sync; it must show no unreviewed overwrite for the targeted files.
   - Use `$axis-ready-review` at the review boundary.

## Output

Report classification, owning layer, registry diff, sign-off or `not triggered`, baseline refresh, contract/consumer tests, visual evidence, unresolved overrides, and provider-specific leakage audit.
