---
name: axis-design-gate
description: Execute the Axis pre-code risk dossier. Use for non-trivial source, test, contract, workflow, tooling, retirement, schema, auth, stack, or broad cross-surface changes before implementation begins.
---

# Axis Design Gate

## Goal

Produce the evidence required by [docs/playbooks/design-gate.md](../../../docs/playbooks/design-gate.md) before code, then hand the current dossier to the selected surface owner.

## Hard gates

Follow [reference.md](../reference.md).
- Do not edit implementation files until the dossier is complete.
- High-risk work stops for explicit user sign-off.
- A trivial bypass states why the policy does not trigger.

## Inputs

- User intent, affected surfaces, and intended files.
- Owner rules and blast-radius search terms.
- Retired identifiers and compatibility requirements, when applicable.

## Workflow

1. Classify risk using [docs/playbooks/design-gate.md § Risk Tiers](../../../docs/playbooks/design-gate.md#risk-tiers).
2. Read [AGENTS.md](../../../AGENTS.md), the Design Gate policy, [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md), and only the touched owner docs.
3. Quote the minimum governing rules with `path:section` references; distinguish enforced, partial, and review-only expectations through [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).
4. Run the smallest `rg` search that covers callers, consumers, tests, docs, generated artifacts, and manifests in scope.
5. For a retirement, apply [docs/playbooks/design-gate.md § Dossier](../../../docs/playbooks/design-gate.md#dossier): name the retired surface, compatibility decision, and pre/post-edit sweep.
6. Record the wire/schema contract decision or `N/A because no wire shape changes`.
7. Name focused development checks and review-boundary verification, then delegate implementation through [README.md § Responsibility catalog](../README.md#responsibility-catalog) with the dossier attached.

## Output

Report risk, governing rules, blast radius, retirement/contract decisions, verification, sign-off status, and next owner. Omit sections that the policy permits as an explicit `N/A because ...` only.
