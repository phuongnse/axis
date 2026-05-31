# Design Gate — mandatory reasoning before code

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← CLAUDE.md](../../CLAUDE.md)

Defects are **born before code is written** — when the agent edits a surface without first re-deriving the rules that govern it (the response is snake_case, the endpoint must go through Application, a required-field change touches every caller, a migration needs a paired Designer). Verification only *catches* what was already written; the Design Gate stops it being written.

The discipline: **re-derive the governing rules for the exact surface you are about to touch, before touching it** — the rigor an independent reviewer applies, applied up front.

> **This gate fails the same way "Gate 1 green" failed** — by being ticked, not done. So it is **artifact-producing**, not a feeling: you quote rules with `file:section`, you paste the blast-radius `grep`, you name the contract. "I thought carefully" is not a Design Gate.

---

## Risk tiers (scales the depth)

| Tier | Examples | Required |
|------|----------|----------|
| **Trivial** | typo, comment, single-line fix, doc-only | No dossier. Still run the gate that applies. |
| **Standard** | intra-module logic, new test, additive UI on an existing API, refactor with no contract change | Short dossier (rules + gate plan). No sign-off needed. |
| **High-risk** | new/changed **endpoint** or **contract/required field**, **migration**/schema, **cross-module** interaction, **auth**, new **library** or public API surface | **Full dossier + user sign-off via plan mode before writing code.** |

When unsure which tier, treat it as the higher one.

---

## The dossier (produce these artifacts, not prose)

For each surface you touch, before coding:

1. **Governing rules** — the rules that constrain this surface, each quoted with its `file:section` source. Go read them; do not rely on memory.
2. **Blast radius** — paste the `grep`/search that lists **every caller, consumer, and test** your change affects. A required-field or response-shape change must update all of them in the same PR.
3. **Contract decision** — name the request/response shape **and casing** explicitly (this repo serializes responses as snake_case — `Program.cs`), and the FE↔BE type that must match.
4. **Gate plan** — the exact commands you will run to verify, at full scope (not a subset).

Skip a row only with an explicit `N/A because …`.

---

## Surface → governing rules (look these up every time)

| If you touch… | Read & quote | Trace (blast radius) | Must pass |
|---------------|--------------|----------------------|-----------|
| **REST endpoint** (new/changed) | snake_case JSON policy (`Program.cs`); no-logic-in-endpoint → `mediator.Send` ([CLAUDE.md](../../CLAUDE.md) · [patterns.md](./patterns.md)); `.RequireAuthorization()`/`AllowAnonymous`; `Produces<T>`/`ProblemDetails` | the FE caller + its types; integration tests for the route | full `dotnet test` (incl. Testcontainers) + the endpoint's test |
| **Contract / required field / response shape** | the validator + the DTO; FE↔BE type parity | **every** caller and test that builds that payload or reads that shape (`grep` the field) | full `dotnet test` + `npm run ci`/`npm test` |
| **EF migration / schema** | [process.md § Infrastructure](./process.md) + [ADR-023](../TECH_STACK.md#adr-023-per-module-ef-core-migrations-only): `dotnet ef` only, paired `.Designer.cs`, snapshot, no `EnsureCreated` | other migrations' ordering; `ApiTestFixture` module DB ([testing.md](./testing.md)) | `dotnet build` + `dotnet format --verify-no-changes` + migration applies |
| **New command/query handler** | [agent-checklist § AC coverage](./agent-checklist.md#ac-coverage--avoid-happy-path-only) | — | matching `*HandlerTests.cs` exists (drift) + tests green |
| **Cross-module interaction** | boundaries: no cross-module SQL / shared `DbContext` / in-proc call ([CLAUDE.md](../../CLAUDE.md) · [patterns.md](./patterns.md)) | the event/gRPC contract + consumers | architecture fitness tests + drift |
| **Frontend data fetch / API client** | [frontend.md](./frontend.md); response casing must match the API (snake_case) | the hook + component consumers | `npm run ci` (tsc + Biome) + `npm test` |
| **New library / public API surface** | tech-stack immutability ([CLAUDE.md](../../CLAUDE.md)) | — | **user approval first** (P0) |
| **Slice of a multi-PR use case** | [pr-slicing.md](./pr-slicing.md): two-sided isolation, shared-seam ownership | siblings that share the seam | both sides of the isolation test |

This table is a starting set, not exhaustive — if your change has a constraint not listed, find its owner doc and quote it anyway.

---

## Sign-off (high-risk only)

Present the dossier through **plan mode** and **do not write code until the user approves**. The human approval is the forcing function that makes the reasoning real for the changes most likely to break. For standard-tier work, the artifacts + the close-the-loop step below are the check.

---

## Close the loop (after implementing)

1. **Self-review the diff against the dossier** — was every governing rule honored, every caller in the blast radius updated, the contract emitted as decided?
2. **Run the gate plan at full scope** — the actual CI commands (`dotnet build` + `dotnet format --verify-no-changes` + full `dotnet test` + `npm run ci` + `npm test` + `./scripts/check-doc-drift.sh`), not a subset. "Build passed" ≠ "CI passed" — formatting, integration, and casing only surface in the full gate. See [agent-checklist § Gate 1](./agent-checklist.md) and [pr-slicing § Gate 1 honesty](./pr-slicing.md#gate-1-honesty).
3. A green claim means you ran it. Anything you could not run (e.g. Testcontainers without Docker) is stated explicitly.

---

## Why this is the root fix

The failures this gate targets were not unknown rules — they were rules **not re-derived at the moment of editing**. CodeRabbit catches them because it re-reads the repo with fresh context every time; the Design Gate moves that same rigor to **before** the code exists, where the fix is free instead of a review round-trip.
