## Summary

<!-- 2–4 sentences: what this PR does overall and why. Do NOT list commits or CI status — the Checks tab covers that. -->

## Linked spec

<!-- Use-case file path(s) or epic README this PR delivers, e.g. docs/use-cases/workflow-engine/execution-management.md -->

## Requirements & rules followed

<!-- Most important first. Tick what applies; mark N/A with a short reason. Order matches the Gates in docs/playbooks/agent-checklist.md. -->

- [ ] **Spec → code** — changes match feature file ACs (or gaps documented in `> **Implementation status**` callouts; deferred items have `**Deferred (PR #N follow-up):**` lines)
- [ ] **Gate 0** — AC map complete; epic/feature docs identified (when shipping code)
- [ ] **Gate 1** — `dotnet build` + `dotnet test` (full `Axis.sln`) + `dotnet format --verify-no-changes` and/or `npm run ci` + `npm run test` ran green for paths touched (N/A with reason)
- [ ] **Gate 2** — docs updated in same PR (US callout, epic README, `PROGRESS.md`, `TECH_STACK` / patterns as triggered); `./scripts/check-doc-drift.sh` ran green locally
- [ ] **Gate 3** — retrospective done; `patterns.md` / feature file / `TECH_STACK.md` / `CLAUDE.md` updated if a new durable rule emerged
- [ ] **Workarounds** — this PR neither introduces nor resolves a P0/P1 violation **OR** [`docs/WORKAROUNDS.md`](../docs/WORKAROUNDS.md) was updated in the same commit and the violation site has a `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>` comment
- [ ] **No new `TODO` / `FIXME` / `NotImplementedException` / placeholder / stub** under `src/`, `tests/`, `frontend/src/`
