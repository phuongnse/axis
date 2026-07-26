---
name: axis-ui-system
description: Govern Axis client composition, semantic component selection, UI source ownership, interaction-state consistency, and safe replacement. Use for experience-contract UI decisions, screen hierarchy, badge/tag/notice choice, read-only versus editable presentation, timelines or detail layouts, hover, highlighted, selected, current, toggled, open, focus, disabled, destructive conventions, semantic tokens, shadcn registry source, components.json, UI baseline, shared visual APIs, primitive exceptions, component providers, or registry/provider upgrades and comparisons.
---

# Axis UI System

## Goal

Make view hierarchy and semantic component choice predictable while keeping registry source replaceable and Axis customization explicit, app-owned, tested, and reviewable.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence.
- Apply the sign-off and provenance rules from [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design); do not broaden or weaken them here.
- Do not refresh the baseline merely to silence unexplained drift or discard existing exception evidence.
- Run owned checks directly; unresolved verification command selection **Delegates** to `$axis-script-scope`.

## Inputs

- Requested UI change and current [docs/playbooks/frontend.md § Component design](../../../docs/playbooks/frontend.md#component-design) contract.
- User task, read/edit mode, content roles, hierarchy, relationships, and responsive/accessibility expectations for a new or changed view.
- An inventory of interactive surfaces classified by state role, plus light/dark representatives for each comparable role.
- Registry config/source, baseline, affected consumers/tests, and provider diff.
- Design Gate and sign-off evidence when triggered.

## Workflow

1. Classify view composition, semantic component consumption, app pattern, theme, registry sync, exception, or provider/style replacement.
2. For a view, consume the experience contract from [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md) before JSX. Confirm mode, hierarchy, relationship structure, semantic component mapping, server-owned product vocabulary versus client-owned interface copy, and responsive/accessibility expectations; audit every badge, tag, card, border, icon, and heading for a distinct job without reopening ordinary product UX choices.
3. Run `python scripts/axis.py check ui-baseline`; trace primitive imports, consumers, tests, provider leakage, raw badge usage, and visual overrides with `rg`. For state changes, record a state-role matrix across every affected interactive surface; distinguish transient, persistent, focus, disabled, and destructive semantics before comparing visuals.
4. For registry work, run the frontend dependency-risk gate, then preview and diff only the named component family through the Axis shadcn wrapper. A CLI/provider dependency change must either remove its advisory or carry current machine-readable acceptance evidence; it never justifies a forced install or unreviewed major override.
5. Choose one owner: upstream registry zone, semantic theme zone, app-owned shared pattern, or one-off feature composition. State visuals outside registry primitives belong only to `frontend/src/components/shared/interactionStates.ts`; reuse its contract and stop when a requested treatment conflicts with the hierarchy.
6. Implement only after required evidence: sync reviewed upstream source; keep shared props Axis-owned and provider-neutral; keep feature classes layout-only; preserve accessibility.
7. Refresh `frontend/ui-baseline.json` only after provenance is established. Preserve valid exceptions; add non-empty `reason` and `signOff` only for approved upstream-zone exceptions.
8. Name required evidence categories: composition contract, semantic component audit, baseline, frontend quality, affected shared contracts/consumers, and post-sync registry diff when applicable; add focused browser evidence for layout or interaction risk. Run owned checks directly and use the verification handoff only when command selection remains unresolved.

## Output

Report classification, owner, view composition and semantic component decisions, the interaction state-role matrix when triggered, provenance/diff, sign-off status, baseline result, contract/consumer evidence, and unresolved exceptions or provider leakage.
