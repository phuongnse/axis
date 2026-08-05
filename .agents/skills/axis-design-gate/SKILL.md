---
name: axis-design-gate
description: Execute the Axis pre-code risk dossier. Use for non-trivial source, test, contract, workflow, tooling, retirement, schema, auth, stack, or broad cross-surface changes before implementation begins.
---

# Axis Design Gate

## Goal

Produce the evidence required by [docs/playbooks/design-gate.md](../../../docs/playbooks/design-gate.md) in the active task before code, then pass it to the selected surface owner without creating a per-change dossier file.

## Hard gates

Follow [reference.md](../reference.md).
- Do not edit implementation files until the dossier is complete.
- High-risk work stops for explicit user sign-off.
- A trivial bypass states why the policy does not trigger.
- A failed path re-enters the Design Gate when a proposed recovery would change the owning contract's owner, execution/trust boundary, invariants, or evidence merely to keep progressing.
- A required enterprise-production concern without current owner and proof blocks implementation; incremental scope does not authorize a temporary foundation.

## Inputs

- User intent, affected surfaces, and intended files.
- Owner rules and blast-radius search terms.
- Retired identifiers, product phase, known consumers/data, and compatibility requirements, when applicable.

## Workflow

1. Classify risk using [docs/playbooks/design-gate.md § Risk Tiers](../../../docs/playbooks/design-gate.md#risk-tiers).
2. Read [AGENTS.md](../../../AGENTS.md), the Design Gate policy, [docs/playbooks/agent-checklist.md](../../../docs/playbooks/agent-checklist.md), and only the touched owner docs.
3. Quote the minimum governing rules with `path:section` references; distinguish enforced, partial, and review-only expectations through [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).
4. Run the smallest `rg` search that covers callers, consumers, tests, docs, generated artifacts, and manifests in scope.
5. Apply [docs/PLATFORM_STRATEGY.md § Enterprise Production Baseline](../../../docs/PLATFORM_STRATEGY.md#enterprise-production-baseline). Classify applicable concerns, map each required concern to an owner and current evidence, and block the slice when a production requirement is missing; separately scoped capabilities may remain out of scope without lowering the implemented boundary.
6. For a retirement, apply [docs/playbooks/design-gate.md § Dossier](../../../docs/playbooks/design-gate.md#dossier): choose clean cutover or evidence-backed compatibility, name the retired surface, and define pre/post-edit sweeps. A clean cutover backed by no supported production consumer/data forbids shims, dual paths, flags, fallbacks, preserved obsolete tests, negative assertions, deny-lists, and routine guidance that keeps the retired surface alive.
7. Record the wire/schema contract decision or `N/A because no wire shape changes`.
8. Name focused development checks and review-boundary verification, then route each current implementation work unit through [reference.md § Agent routing](../reference.md#agent-routing) and [README.md § Responsibility catalog](../README.md#responsibility-catalog) with the dossier attached. Re-evaluate unexecuted units when a later decision makes them bounded. Keep sign-off status in this transient handoff; promote only current durable decisions and rationale to their owner docs, not actor, date, conversation provenance, or a committed per-change dossier.
9. When re-entering after a failure, record the root cause and compare the required contract with the proposed path across owner, execution/trust boundary, invariants, and evidence. Reject a path that changes those dimensions only to bypass the failure; otherwise record the explicit contract decision and the proof required for the resulting boundary.

## Output

Report risk, governing rules, blast radius, enterprise-production fitness, retirement/contract decisions, verification, sign-off status, and next owner. Omit sections that the policy permits as an explicit `N/A because ...` only.
