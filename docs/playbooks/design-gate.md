# Design Gate

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

The Design Gate is required pre-code evidence for non-trivial changes. Produce the per-change dossier in the active task, pass it through typed owner handoffs, and retain it until the change closes. Promote only current durable decisions and rationale to owner docs; keep task progress and review context in checkpoint commits or the pull request. Do not create committed or ignored per-change dossier files. This is the sole committed Design Gate playbook, not a machine-enforced CI gate.

For repeatable execution, read [`.agents/skills/assess-design/SKILL.md`](../../.agents/skills/assess-design/SKILL.md) (`$assess-design`).

---

## Risk Tiers

| Tier | Examples | Required |
|------|----------|----------|
| **Trivial** | typo, comment, single-line correction, doc-only | No dossier. Still run the triggered check. |
| **Standard** | intra-module logic, new test, additive UI on an existing API, refactor with no contract change | Compact or full dossier by blast radius. No sign-off needed. |
| **High-risk** | new/changed endpoint, contract/required field, migration/schema, auth, new/replaced runtime, framework, service, major library, public API surface | Full dossier + user sign-off before code. |

When unsure which tier, treat it as the higher one.

---

## Dossier

Scale the dossier to the risk before coding:

- **Compact** for localized standard work with no retirement, wire/schema/auth/stack change, or broad cross-surface blast radius.
- **Full** for high-risk work and for standard work that retires a supported surface, changes deterministic checks or workflow behavior, or spans multiple ownership surfaces.

Every non-trivial dossier covers:

1. **Governing rules** — quote the owner rules with `file:section`; do not rely on memory.
2. **Blast radius** — paste the `rg` search that lists affected callers, consumers, tests, docs, and generated artifacts.
3. **Enterprise production fitness** — apply [docs/PLATFORM_STRATEGY.md § Enterprise Production Baseline](../PLATFORM_STRATEGY.md#enterprise-production-baseline) to the slice. Classify every applicable concern, name its owning contract and evidence, and give an owner-backed reason for `N/A`. A required concern without current proof blocks implementation; it cannot be moved to a follow-up merely because the capability is being delivered incrementally.
4. **Retirement and compatibility contract** — for removals, renames, replacements, drops, disables, deprecations, or other retirements, name the product phase, known consumers/data, and one explicit decision:
   - `Clean cutover`: compatibility is not required; remove the old implementation, wire shape, callers, generated artifacts, tests, docs, flags, and fallbacks in the same slice.
   - `Compatibility required`: name the exact consumer/data constraint, supported overlap, owner, removal condition, and proving tests.

   In both cases list retired identifiers and the post-edit `rg` sweep. For a clean cutover, also name the exact positive invariant over the current finite registry, package, dependency graph, or contract that rejects every extra entry without storing retired identifiers; if no finite owner exists, record why the transient sweep is the only structurally valid proof. A dossier is incomplete if it substitutes a committed retired-name denylist, regression fixture, or compatibility test for that current-owner proof. Do not add compatibility “just in case.” Otherwise write `N/A because no supported surface is retired`.
5. **Contract decision** — name request/response shape, schema, casing, FE/BE type parity, or write `N/A because no wire shape changes`.
6. **Verification plan** — list exact development checks and review-checks checks. Do not call review-only artifacts gates.
7. **UI review unit** — for constitution, profile, theme, surface-owner, or visible-consumer work, name exactly one foundation, owner, or consumer; choose the largest coherent boundary with one owner, contract, invalidation set, and review decision, normally the whole foundation/surface or a complete consumer. Do not fragment one owner into typography, spacing, individual regions, or other implementation details unless ownership, acceptance, verification, or safe rollback is genuinely independent; record that rationale when splitting. Name the candidate review boundary, applicable profile requirements and current gaps, invalidation triggers, and the conditions that stop and reopen the unit. Otherwise write `N/A because the change does not affect visible UI or its conformance system`.

For project-owner communication, collapse the manifest's independent lifecycle, acceptance, and perceptual controls into one review-unit roll-up: `In progress`, `Awaiting review`, or `Complete`. Requested changes return the same unit to `In progress`. Raw machine states remain audit detail and must not be presented as three separate decisions for the reviewer.

Skip a row only with an explicit `N/A because ...`.

---

## Surface Routing

The managed capability skills pinned by `.process/process.lock` own reusable routing.
After the dossier, continue with the selected product or technical owner and retain
the Design Gate evidence; do not restart the gate while that evidence remains current.

---

## Sign-Off

High-risk surfaces require user sign-off before code. For standard-tier work, the dossier plus close-the-loop self-review is enough.

---

## Close The Loop

1. Self-review the diff against the dossier.
2. Run the triggered checks from [docs/playbooks/agent-checklist.md § Review Verification](./agent-checklist.md#review-verification).
3. If you claim the full suite ran locally, it must mean full `python scripts/axis.py dotnet test` ran, including integration tests.
