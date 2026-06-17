# Design Gate — mandatory reasoning before code

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← AGENTS.md](../../AGENTS.md)

The Design Gate is a required pre-code review artifact for non-trivial changes. It is not a machine-enforced CI gate; it is evidence that the agent re-derived the rules for the exact surface before editing it.

For repeatable execution, use `$axis-design-gate`.

---

## Risk tiers (scales the depth)

| Tier | Examples | Required |
|------|----------|----------|
| **Trivial** | typo, comment, single-line fix, doc-only | No dossier. Still run the gate that applies. |
| **Standard** | intra-module logic, new test, additive UI on an existing API, refactor with no contract change | Short dossier (rules + gate plan). No sign-off needed. |
| **High-risk** | new/changed **endpoint** or **contract/required field**, **migration**/schema, **cross-module** interaction, **auth**, new **library** or public API surface | **Full dossier + user sign-off via plan mode before writing code.** |

When unsure which tier, treat it as the higher one.

---

## Dossier

For each surface you touch, before coding:

1. **Governing rules** — the rules that constrain this surface, each quoted with its `file:section` source. Go read them; do not rely on memory.
2. **Blast radius** — paste the `grep`/search that lists **every caller, consumer, and test** your change affects. A required-field or response-shape change must update all of them in the same PR.
3. **Contract decision** — name the request/response shape **and casing** explicitly (this repo serializes JSON API contracts as camelCase — `Program.cs`), and the FE↔BE type that must match.
4. **Gate plan** — the exact commands you will run to verify, at full scope (not a subset).

Skip a row only with an explicit `N/A because …`.

---

## Surface → governing rules (look these up every time)

| If you touch… | Read & quote | Trace (blast radius) | Must pass |
|---------------|--------------|----------------------|-----------|
| **REST endpoint** (new/changed) | camelCase JSON policy (`Program.cs`); no-logic-in-endpoint → `mediator.Send` ([AGENTS.md](../../AGENTS.md) · [patterns.md](./patterns.md)); `.RequireAuthorization()`/`AllowAnonymous`; `Produces<T>`/`ProblemDetails` | the FE caller + its types; integration tests for the route | full `dotnet test` (incl. Testcontainers) + the endpoint's test |
| **Contract / required field / response shape** | the validator + the DTO; FE↔BE type parity | **every** caller and test that builds that payload or reads that shape (`grep` the field) | full `dotnet test` + `npm run ci`/`npm test` |
| **EF migration / schema** | [process.md § Infrastructure](./process.md) + [ADR-023](../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only): `dotnet ef` only, paired `.Designer.cs`, snapshot, no `EnsureCreated` | other migrations' ordering; `ApiTestFixture` module DB ([testing.md](./testing.md)) | `dotnet build` + `dotnet format --verify-no-changes` + migration applies |
| **New command/query handler** | [agent-checklist § AC coverage](./agent-checklist.md#ac-coverage--avoid-happy-path-only) | — | matching `*HandlerTests.cs` exists (drift) + tests green |
| **Cross-module interaction** | boundaries: no cross-module SQL / shared `DbContext` / in-proc call ([AGENTS.md](../../AGENTS.md) · [patterns.md](./patterns.md)) | the event/gRPC contract + consumers | architecture fitness tests + drift |
| **Frontend data fetch / API client** | [frontend.md](./frontend.md); response casing must match the API (camelCase) | the hook + component consumers | `npm run ci` (tsc + Biome) + `npm test` |
| **New library / public API surface** | tech-stack immutability ([AGENTS.md](../../AGENTS.md)) | — | **user approval first** (P0) |
| **Slice of a multi-PR use case** | [pr-slicing.md](./pr-slicing.md): two-sided isolation, shared-seam ownership | siblings that share the seam | both sides of the isolation test |

This table is a starting set, not exhaustive — if your change has a constraint not listed, find its owner doc and quote it anyway.

---

## Sign-off (high-risk only)

Present the dossier through **plan mode** and **do not write code until the user approves**. The human approval is the forcing function that makes the reasoning real for the changes most likely to break. For standard-tier work, the artifacts + the close-the-loop step below are the check.

---

## Close The Loop

1. **Self-review the diff against the dossier** — was every governing rule honored, every caller in the blast radius updated, the contract emitted as decided?
2. **Run the local gate before review** — `python scripts/axis.py verify` runs build, vulnerable package scan, format, unit test projects, frontend checks, policy tests, and doc drift. The pre-push hook is only a quick policy/doc sanity gate for ordinary network pushes. CI/branch protection is the required full gate and runs full `dotnet test` including Testcontainers before merge. "Build passed" ≠ "PR is mergeable" — formatting, integration, and casing only fully surface in the CI gate. See [agent-checklist § Verification Gate](./agent-checklist.md#verification-gate--verify-before-pr-review) and [pr-slicing § Verification Gate Honesty](./pr-slicing.md#verification-gate-honesty).
3. If you claim the **full suite** ran locally, that means full `dotnet test Axis.sln` ran — including the integration tests. Docker is required for that full local run; if Docker is unavailable, rely on CI for the authoritative full gate instead of presenting a partial run as full.
