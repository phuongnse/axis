# Review findings ledger

> **Navigation**: [← docs/README.md](./README.md) · [← CLAUDE.md](../CLAUDE.md)

A register of recurring code-review finding **classes** and how each is
prevented. The goal: a given finding class is reviewed by a human (or
CodeRabbit) **once**; after that the project either mechanizes it away
(analyzer / fitness test / codegen / CI guard) or records a deliberate
decision to keep catching it in review. This turns review feedback into
prevention instead of re-explaining the same rule every PR.

Sibling register: [WORKAROUNDS.md](./WORKAROUNDS.md) (intentional rule
violations). This file is the inverse — rules we are progressively making
impossible to violate.

---

## When to add a row

Wired into **Gate 3** ([agent-checklist.md § Gate 3](./playbooks/agent-checklist.md)):
when a review finding **repeats a class already seen** in a prior PR, it must
land here — either pointed at a mechanism or recorded as deliberately manual.

Before building a gate, the class must pass all three tests. If any is "no",
leave it in the **manual** tier with the reason; an unreliable gate costs more
than the review it replaces.

1. **Deterministic?** Can a rule decide it without understanding intent? (A
   dropped `CancellationToken` is; "this should be a `Result<T>`" often is not.)
2. **Recurred?** Seen ≥ 2–3 times across PRs — a real pattern, not a one-off.
3. **Cheaper than re-review?** Gate build + maintenance (incl. false-positive
   noise) is less than the cost of flagging it forever.

Mechanism tiers, cheapest first: **existing analyzer rule** (just escalate
severity in [`.editorconfig`](../.editorconfig)) → **architecture fitness test**
([tests/Architecture](../tests/Architecture/Axis.Architecture.Tests/README.md))
→ **codegen / source generator** → **CI guard** (workflow step or paths-filter)
→ **manual** (CodeRabbit path-instruction in `.coderabbit.yaml`, added by the
CodeRabbit-config PR, + reviewer judgment).

---

## Ledger

| Finding class | Mechanism | Tier | Status |
|---|---|---|---|
| FE/BE casing & wire-shape drift | `gen:api-types` from `openapi.json` + `OpenApiDocumentTests` + paths-filter on `openapi.json` | codegen + CI | **Closed** ([#165](https://github.com/phuongnse/axis/pull/165)) |
| `CancellationToken` not forwarded to a callee that accepts one | `CA2016` escalated to `warning` → build error via `TreatWarningsAsErrors` | analyzer | **Closed** (`.editorconfig`) |
| Cross-module in-process call / illegal project ref | Architecture fitness tests + CodeRabbit path-instruction | arch-test + manual | **Partial** — fitness tests miss runtime DI resolved via `Contracts`; CodeRabbit flags the rest |
| One public type per `.cs` file | — (kept at `suggestion`) | manual | **Won't mechanize** — not a defect class; a hard rule fights intentional groupings (`Result<T>`, `ICommand<T>`, polymorphic VO hierarchies, query+DTO colocation). 2026-06 scan: 22 files, mostly idiomatic |
| `using` not fully-qualified names | `IDE0001`/`IDE0002` (currently `suggestion`) | analyzer | **Planned** (low ROI — no real defect) |
| Auth/permission check before input validation | CodeRabbit path-instruction (`src/Axis.Api/Endpoints`) | manual | **Manual** — ordering intent not reliably analyzable |
| Wrong status code on bad input (400 vs 500) | CodeRabbit path-instruction | manual | **Manual** |
| Side effects committed before the DB transaction they depend on | CodeRabbit path-instruction | manual | **Manual** — needs data-flow judgment |
| Test asserts something other than its name claims (e.g. `Returns403` asserts 401) | CodeRabbit path-instruction; analyzer pilot under consideration | manual → analyzer? | **Manual** — green-but-wrong test; pilot a name↔asserted-status analyzer if it recurs |
| `Result`/`Result<T>` vs bespoke bool/tuple/throw for business failures | CodeRabbit path-instruction | manual | **Manual** (P1, design judgment) |
| Endpoint returns `object`/anonymous JSON instead of an Application-layer DTO | `check-doc-drift.sh` grep guard (added-lines ratchet): bans new `.Produces<object>` / `Results.Ok(new { … })` in `Axis.Api/Endpoints` | CI guard | **Closed** — 30 endpoints converted to named DTOs (`CreatedResponse`/`MessageResponse`, query DTOs, `UserSessionResponse`); `openapi.json`/`api-types.ts` regenerated. Flagged on [#155](https://github.com/phuongnse/axis/pull/155) |
| Minimal-API endpoint orchestrates >1 `mediator.Send` (logic in endpoint; side-effect consumed before a later step can fail) | `check-doc-drift.sh` full-state guard: fails if any endpoint handler (returning `Task<IResult>`) has >1 `.Send(`/`.Publish(` | CI guard | **Closed** — baseline 0; fails build on any new. Drift guard over a Roslyn analyzer: cheaper, no new dependency for one rule. Limit: named handlers only, not inline lambdas. Flagged on [#155](https://github.com/phuongnse/axis/pull/155) |

Status: **Closed** = a mechanism fails the build/CI; **Partial** = mechanized
with a documented gap; **Planned** = agreed, not yet built; **Manual** = kept in
review on purpose (reason in the row).

---

## Metric

The point is a downward trend in **findings per PR for already-closed classes**
— ideally zero (a closed class reappearing means the gate has a hole, like the
`openapi.json` paths-filter gap [#165] closed). Note repeat-of-closed-class
occurrences in the Gate 3 retrospective so holes surface instead of being
re-fixed by hand.
