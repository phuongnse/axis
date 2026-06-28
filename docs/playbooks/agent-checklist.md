# Agent Checklist

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Review checklist only. Workflow lives in repo skills ([`.cursor/skills/README.md`](../../.cursor/skills/README.md) and [`.cursor/skills/reference.md`](../../.cursor/skills/reference.md)); enforcement status lives in [docs/ENFORCEMENT.md](../ENFORCEMENT.md); command behavior lives in [docs/playbooks/scripts.md](./scripts.md).

## Before Code

- Use `$axis-design-gate` for non-trivial work; high-risk surfaces need sign-off.
- Read the owning use-case/domain docs and same-module code.
- Map in-scope ACs before behavior work.
- Resolve or explicitly defer lower-layer gaps before API work.

## Acceptance Coverage

Use-case ACs are the contract. Cover validation, edge, authorization, isolation, and dependency-failure paths when in scope.

AC map: `AC | kind | surface | proving test or exact deferral`.

No blank in-scope rows; required AT rows name automated runners; implementation details stay out of spec matrices; incomplete in-scope ACs block `Done`.

## Review Verification

During development, run the smallest check that proves the edit. Before review, use `$axis-ready-review`.

Only claim a full local suite when full `python scripts/axis.py dotnet test` ran, including integration/API tests. CI remains authoritative before merge.

## Docs Review

| Trigger | Owner |
|---|---|
| Behavior/spec/status | Owning use case |
| Stack/library/manifests | [docs/TECH_STACK.md](../TECH_STACK.md) and owning manifests |
| Repeated rule class | Focused playbook or [docs/ENFORCEMENT.md](../ENFORCEMENT.md) |
| Visual/source/preview | `$axis-visual-artifact` |

Pure refactor/style/test-only changes can report docs as not triggered.

## Retrospective Review

Use `$axis-ready-review`. Update the owner when the diff creates a new rule, invariant, stack baseline drift, review gap, spec gap, or repeat finding.

## Layer Status

Layer status format lives in [docs/playbooks/docs-style.md](./docs-style.md#implementation-status). Never combine `Done` with pending work.
