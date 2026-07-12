# Design Gate

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

The Design Gate is a required pre-code review artifact for non-trivial changes. It is not a machine-enforced CI gate; it is evidence that the agent re-derived the rules for the exact surface before editing it.

For repeatable execution, read [`.agents/skills/axis-design-gate/SKILL.md`](../../.agents/skills/axis-design-gate/SKILL.md) (`$axis-design-gate`).

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
3. **Retirement contract** — for removals, renames, replacements, drops, disables, deprecations, or other retirements, list retired identifiers, compatibility exceptions, and the post-edit `rg` sweep; otherwise write `N/A because no supported surface is retired`.
4. **Contract decision** — name request/response shape, schema, casing, FE/BE type parity, or write `N/A because no wire shape changes`.
5. **Verification plan** — list exact development checks and ready-review checks. Do not call review-only artifacts gates.

Skip a row only with an explicit `N/A because ...`.

---

## Surface Routing

Use the matching repo skill for surface-specific checklist detail:

| Surface | Skill |
|---|---|
| Missing or incomplete use-case spec | `$axis-use-case-spec` |
| New module / DDD / CQRS / event sourcing architecture | `$axis-module-architecture` |
| Tactical DDD / CQRS / persistence / event pattern implementation | `$axis-module-patterns` |
| Use-case slice | `$axis-use-case-implementation` |
| REST/OpenAPI/API type change | `$axis-api-contract` |
| App shell / shared SPA UI infrastructure | `$axis-frontend-foundation` |
| Frontend feature or SPA caller | `$axis-frontend-feature` |
| UI customization / shadcn sync / component provider | `$axis-ui-system` |
| Mermaid or generated visual artifact | `$axis-visual-artifact` |
| Review feedback | `$axis-review-feedback` |

If no skill fits, quote the owner doc directly and keep the same dossier scale.

---

## Sign-Off

High-risk surfaces require user sign-off before code. For standard-tier work, the dossier plus close-the-loop self-review is enough.

---

## Close The Loop

1. Self-review the diff against the dossier.
2. Run the triggered checks from [docs/playbooks/agent-checklist.md § Review Verification](./agent-checklist.md#review-verification).
3. If you claim the full suite ran locally, it must mean full `python scripts/axis.py dotnet test` ran, including integration tests.
