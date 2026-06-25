# Agent Checklist

> **Navigation**: [<- docs/README.md](../README.md) . [<- AGENTS.md](../../AGENTS.md)

Daily index only. Workflow lives in repo skills; enforcement status lives in [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md); command behavior lives in `scripts/axis.py`.

## Skill Routing

| Work | Skill |
|---|---|
| Docs | `$axis-doc-hygiene` |
| Script scope | `$axis-script-scope` |
| Spec | `$axis-use-case-spec` |
| Use-case code | `$axis-use-case-implementation` |
| REST/OpenAPI | `$axis-api-contract` |
| Events/gRPC/Wolverine | `$axis-cross-module-contract` |
| Frontend | `$axis-frontend-feature` |
| Design system | `$axis-design-system` |
| Visuals | `$axis-visual-artifact` |
| Feedback | `$axis-review-feedback` |
| Review ready? | `$axis-ready-review` |
| PR action | `$axis-pull-request` |

## Ready Review - before code

- Run `$axis-design-gate` for non-trivial work; high-risk surfaces need sign-off.
- Read owning use-case/domain docs and same-module code.
- Check `docs/WORKAROUNDS.md` when touching known shortcut areas.
- Build an AC map before behavior work.
- Resolve or explicitly defer lower-layer gaps before API work.

## AC coverage — avoid happy-path-only

Use-case ACs are the contract. Cover validation, edge, permission, isolation, and dependency-failure paths when in scope.

AC map: `AC | kind | surface | proving test or exact deferral`.

Rules: no blank in-scope rows; required AT rows name automated runners; implementation details stay out of spec matrices; spec checkboxes are not progress; incomplete in-scope ACs block `✅`.

## Verification Gate — verify before PR review

During development, run the smallest check that proves the edit. Do not run the ready-review gate after every small change.

Before review, use `$axis-ready-review`; it classifies changed paths, runs triggered verification once, checks docs/status/workarounds, and reports pass/fail/not-run honestly.

Only claim a full local suite when full `python scripts/axis.py dotnet test` ran, including integration/API tests. CI remains authoritative before merge.

## Docs Review

| Trigger | Owner |
|---|---|
| Behavior/spec/status | Owning use case; domain README/`PROGRESS.md` only when summaries change |
| Stack/library | `docs/TECH_STACK.md` |
| Repeated finding | Focused playbook or `docs/REVIEW_FINDINGS.md` |
| P0/P1 shortcut | `docs/WORKAROUNDS.md` plus site reference |
| Visual/source/preview | `$axis-visual-artifact` |

Pure refactor/style/dependency/test-only changes can report docs as not triggered.

## Retrospective Review

Use `$axis-ready-review` and answer: new rule, invented invariant, infrastructure footgun, non-obvious test setup, direction change, review shortcut, spec gap, incident-level rule text, repeat finding.

If any answer is `Yes`, update the owning doc, test, use-case, or finding row.

## Layer status

| Symbol | Meaning |
|---|---|
| `✅` | In-scope layer ACs done |
| `⚠️` | Started; exact gaps listed |
| `⏳` | Not started |
| `N/A` | Layer does not apply |

Never combine `✅` with pending work. Format lives in [docs-style](./docs-style.md#implementation-status-after-each-us-ac-block).

## Playbooks - open only when needed

Design Gate: [design-gate.md](./design-gate.md). Layout discovery: [repo-layout-discovery.md](./repo-layout-discovery.md). Pattern owner: [patterns-index.md](./patterns-index.md). Scripts: [scripts.md](./scripts.md). Docs shape: [docs-style.md](./docs-style.md).
