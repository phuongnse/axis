# Agent Checklist

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Review checklist only. Workflow lives in the managed `run-change` and capability
skills pinned by `.process/process.lock`; enforcement status lives in
[docs/ENFORCEMENT.md](../ENFORCEMENT.md), and command behavior lives in
[docs/playbooks/scripts.md](./scripts.md).

## Before Code

- Use `$assess-design` for non-trivial work; high-risk surfaces need sign-off.
- Select the applicable managed capability skill and preserve current prerequisite
  evidence across ownership handoffs.
- Confirm the routing checkpoint covers current independently ownable work units; re-evaluate unexecuted units after decisions resolve ambiguity or change their scope, ownership, or verification boundary.
- Read the owning use-case, foundation, or domain docs and same-module code.
- Map in-scope ACs before behavior work.
- Map applicable enterprise-production concerns to an owning contract and current proof; a required security, isolation, data, recovery, deployment, operability, capacity, accessibility, maintenance, or compatibility concern cannot be deferred as incremental delivery.
- Resolve or explicitly defer lower-layer gaps before API work.
- For a retirement, confirm the Design Gate chose clean cutover or named a real compatibility constraint; do not infer compatibility.
- For visible UI, confirm one review unit is declared at the largest coherent owner/contract/invalidation/review boundary; do not fragment one owner by typography, spacing, or individual region without an explicit independent-ownership, acceptance, verification, or rollback rationale. Confirm the current versioned profile is an exact covered/gap/not-applicable partition, every covered requirement traces to owning acceptance and evidence, candidate evidence is not recorded as accepted, and no next independently owned consumer is bundled before project-owner acceptance.
- Report that UI unit to the project owner with one roll-up only: `In progress`, `Awaiting review`, or `Complete`. Keep raw lifecycle/review/perceptual values as audit detail, not three reviewer-facing statuses.

## Acceptance Coverage

Use [`docs/playbooks/acceptance-authoring.md`](./acceptance-authoring.md) for AC/AT schema. Review that validation, edge, authorization, isolation, dependency-failure, screen, accessibility, and interaction expectations are covered when in scope.

AC map: `AC | kind | surface | proving test or exact deferral`.

No blank in-scope rows; required AT rows name verification categories; incomplete in-scope ACs block `Done`.

Confirm one primary actor goal, explicit preconditions/success/minimal guarantees, implementation-agnostic flows, one owner for shared invariants, independently reportable AC outcomes, and cohesive AT scenarios. Title conjunctions, alternate actors, implementation nouns, and clause counts are review signals rather than deterministic failures.

Acceptance evidence proves the production semantics of the implemented slice. Local/test adapters may change infrastructure values, but evidence that bypasses or substitutes the required trust, persistence, concurrency, failure, or recovery boundary is invalid.

For a high-risk security or privacy protocol, confirm the current threat model names assets, entry points, trust boundaries, abuse cases, mitigations, and tests; a failure-mode list alone is not sufficient.

## Review Verification

During development, run the smallest check that proves the edit. Before independent
review, use `processctl change verify` for every required profile.

Only claim a full local suite when full `python scripts/axis.py dotnet test` ran, including integration/API tests. CI remains authoritative before merge.

Browser evidence must name focused, independently runnable journeys triggered by the diff and owning acceptance evidence. Reject fixed sleeps, local test-timeout overrides, serial state, and monolithic journeys that duplicate component/API assertions.

For a clean cutover, review the post-edit `rg` sweep for retired routes, fields, components, flags, fallbacks, generated types, tests, and guidance. A passing new path does not compensate for an old path left behind.

## Docs Review

| Trigger | Owner |
|---|---|
| Behavior/spec/status | Owning use case |
| Stack/library/manifests | [docs/TECH_STACK.md](../TECH_STACK.md) and owning manifests |
| Repeated rule class | Focused playbook or [docs/ENFORCEMENT.md](../ENFORCEMENT.md) |
| Mermaid or committed visual artifact | `$maintain-docs` and the owning spec |

Pure refactor/style/test-only changes can report docs as not triggered.

## Retrospective Review

Use the shared `verify-change`, `review-change`, and `finish-change` gates and apply
the managed `evolve-process` classification and regression loop.
Record one outcome instead of adding retrospective prose.

## Layer Status

Layer status format lives in [docs/playbooks/docs-style.md](./docs-style.md#implementation-status). Never combine `Done` with pending work.
