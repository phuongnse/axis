# Agent Checklist

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Review checklist only. Workflow lives in repo skills ([`.agents/skills/README.md`](../../.agents/skills/README.md) and [`.agents/skills/reference.md`](../../.agents/skills/reference.md)); enforcement status lives in [docs/ENFORCEMENT.md](../ENFORCEMENT.md); command behavior lives in [docs/playbooks/scripts.md](./scripts.md).

## Before Code

- Use `$axis-design-gate` for non-trivial work; high-risk surfaces need sign-off.
- Select the entry owner from [`.agents/skills/README.md`](../../.agents/skills/README.md) and preserve current prerequisite evidence across typed handoffs.
- Confirm the routing checkpoint covers current independently ownable work units; re-evaluate unexecuted units after decisions resolve ambiguity or change their scope, ownership, or verification boundary.
- Read the owning use-case, foundation, or domain docs and same-module code.
- Map in-scope ACs before behavior work.
- Resolve or explicitly defer lower-layer gaps before API work.
- For a retirement, confirm the Design Gate chose clean cutover or named a real compatibility constraint; do not infer compatibility.

## Acceptance Coverage

Use [`.agents/skills/axis-use-case-spec/reference.md`](../../.agents/skills/axis-use-case-spec/reference.md) for AC/AT schema. Review that validation, edge, authorization, isolation, dependency-failure, screen, accessibility, and interaction expectations are covered when in scope.

AC map: `AC | kind | surface | proving test or exact deferral`.

No blank in-scope rows; required AT rows name verification categories; incomplete in-scope ACs block `Done`.

## Review Verification

During development, run the smallest check that proves the edit. Before independent review, use `$axis-review-readiness`.

Only claim a full local suite when full `python scripts/axis.py dotnet test` ran, including integration/API tests. CI remains authoritative before merge.

Browser evidence must name focused, independently runnable journeys triggered by the diff and owning acceptance evidence. Reject fixed sleeps, local test-timeout overrides, serial state, and monolithic journeys that duplicate component/API assertions.

For a clean cutover, review the post-edit `rg` sweep for retired routes, fields, components, flags, fallbacks, generated types, tests, and guidance. A passing new path does not compensate for an old path left behind.

## Docs Review

| Trigger | Owner |
|---|---|
| Behavior/spec/status | Owning use case |
| Stack/library/manifests | [docs/TECH_STACK.md](../TECH_STACK.md) and owning manifests |
| Repeated rule class | Focused playbook or [docs/ENFORCEMENT.md](../ENFORCEMENT.md) |
| Mermaid or committed visual artifact | `$axis-doc-hygiene` and the owning spec |

Pure refactor/style/test-only changes can report docs as not triggered.

## Retrospective Review

Use `$axis-review-readiness` and apply [`.agents/skills/reference.md § Improvement loop`](../../.agents/skills/reference.md#improvement-loop). Record one outcome instead of adding retrospective prose.

## Layer Status

Layer status format lives in [docs/playbooks/docs-style.md](./docs-style.md#implementation-status). Never combine `Done` with pending work.
