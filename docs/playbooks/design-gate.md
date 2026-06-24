# Design Gate — mandatory reasoning before code

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

The Design Gate is a required pre-code review artifact for non-trivial changes. It is not a machine-enforced CI gate; it is evidence that the agent re-derived the rules for the exact surface before editing it.

For repeatable execution, use `$axis-design-gate`.

---

## Risk Tiers

| Tier | Examples | Required |
|------|----------|----------|
| **Trivial** | typo, comment, single-line fix, doc-only | No dossier. Still run the triggered check. |
| **Standard** | intra-module logic, new test, additive UI on an existing API, refactor with no contract change | Short dossier. No sign-off needed. |
| **High-risk** | new/changed endpoint, contract/required field, migration/schema, cross-module interaction, auth, new library, public API surface | Full dossier + user sign-off before code. |

When unsure which tier, treat it as the higher one.

---

## Dossier

For each surface you touch, before coding:

1. **Governing rules** — quote the owner rules with `file:section`; do not rely on memory.
2. **Blast radius** — paste the `rg` search that lists affected callers, consumers, tests, docs, and generated artifacts.
3. **Contract decision** — name request/response shape, schema, casing, FE/BE type parity, or write `N/A because no wire shape changes`.
4. **Verification plan** — list exact development checks and ready-review checks. Do not call review-only artifacts gates.

Skip a row only with an explicit `N/A because ...`.

---

## Surface Routing

Use the matching repo skill for surface-specific checklist detail:

| Surface | Skill |
|---|---|
| Missing or incomplete use-case spec | `$axis-use-case-spec` |
| Use-case slice | `$axis-use-case-implementation` |
| REST/OpenAPI/API type change | `$axis-api-contract` |
| Event, proto, Wolverine, Kafka, RabbitMQ, or gRPC | `$axis-cross-module-contract` |
| Frontend feature or SPA caller | `$axis-frontend-feature` |
| Design source, legacy wireframe, Mermaid, generated visual artifact | `$axis-visual-artifact` |
| Review feedback | `$axis-review-feedback` |

If no skill fits, quote the owner doc directly and keep the same dossier shape.

---

## Sign-Off

High-risk surfaces require user sign-off before code. For standard-tier work, the dossier plus close-the-loop self-review is enough.

---

## Close The Loop

1. Self-review the diff against the dossier.
2. Run the triggered checks from [agent-checklist § Verification Gate](./agent-checklist.md#verification-gate--verify-before-pr-review).
3. If you claim the full suite ran locally, it must mean full `python scripts/axis.py dotnet test` ran, including integration tests.
