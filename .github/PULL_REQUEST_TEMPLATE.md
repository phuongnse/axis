## Summary

<!-- 2–4 sentences: what this PR does overall and why. Do NOT list commits or CI status — the Checks tab covers that. -->

## Linked spec

<!-- Use-case file path(s) or domain README this PR delivers, e.g. docs/use-cases/workflow-engine/start-execution/README.md -->

## Requirements & rules followed

<!-- Most important first. Tick what applies; mark N/A with a short reason. Order matches the Gates in docs/playbooks/agent-checklist.md. -->

- [ ] **Spec → code** — changes match use-case file ACs (or gaps documented in `> **Implementation status**` callouts; deferred items have `**Deferred (PR #N follow-up):**` lines)
- [ ] **Gate 0** — AC map complete; use-case/domain docs identified (when shipping code)
- [ ] **Path coverage matrix** — for each touched implementation surface, happy/validation/auth/isolation/dependency paths are tested or explicitly marked `N/A` (see `docs/playbooks/agent-checklist.md` § AC coverage)
- [ ] **Gate 1** — local fast gate (`scripts/verify.sh`: build + vulnerability scan + format + unit test projects + frontend checks + doc drift) ran green for paths touched; full `dotnet test Axis.sln` is enforced by CI/branch protection (N/A with reason)
- [ ] **Gate 2** — docs updated in same PR (use-case callout, domain README, `PROGRESS.md`, `TECH_STACK` / patterns as triggered); `./scripts/check-doc-drift.sh` ran green locally
- [ ] **Gate 3** — retrospective done; `patterns.md` / use-case file / `TECH_STACK.md` / `CLAUDE.md` updated if a new durable rule emerged
- [ ] **Workarounds** — this PR neither introduces nor resolves a P0/P1 violation **OR** [`docs/WORKAROUNDS.md`](../docs/WORKAROUNDS.md) was updated in the same commit and the violation site has a `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>` comment
- [ ] **No new `TODO` / `FIXME` / `NotImplementedException` / placeholder / stub** under `src/`, `tests/`, `frontend/src/`
