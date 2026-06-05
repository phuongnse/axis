## Summary

<!-- 2–4 sentences: what this PR does overall and why. Do NOT list commits or CI status — the Checks tab covers that. -->

## Linked spec

<!-- Use-case file path(s) or domain README this PR delivers, e.g. docs/use-cases/workflow-engine/start-execution/README.md -->

## Requirements & rules followed

<!-- Most important first. Tick what applies; mark N/A with a short reason. Order matches the review/check flow in docs/playbooks/agent-checklist.md. -->

- [ ] **Spec → code** — changes match use-case file ACs (or gaps documented in `> **Implementation status**` callouts; deferred items have `**Deferred (PR #N follow-up):**` lines)
- [ ] **Ready review** — AC map complete; use-case/domain docs identified (when shipping behavior)
- [ ] **Path coverage matrix** — for each touched implementation surface, happy/validation/auth/isolation/dependency paths are tested or explicitly marked `N/A` (see `docs/playbooks/agent-checklist.md` § AC coverage)
- [ ] **Verification gate** — triggered local fast-gate commands from `docs/playbooks/agent-checklist.md` ran green for paths touched; full `dotnet test Axis.sln` is enforced by CI/branch protection (N/A with reason)
- [ ] **Docs review** — owning docs updated when behavior/spec/status changed; pure refactor/style/test-only changes marked N/A
- [ ] **Retrospective review** — `REVIEW_FINDINGS.md` / patterns / use-case / `TECH_STACK.md` / `CLAUDE.md` updated if a durable rule or repeat finding emerged
- [ ] **Workarounds** — this PR neither introduces nor resolves a P0/P1 violation **OR** [`docs/WORKAROUNDS.md`](../docs/WORKAROUNDS.md) was updated in the same commit and the violation site has a `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>` comment
- [ ] **No new `TODO` / `FIXME` / `NotImplementedException` / placeholder / stub** under `src/`, `tests/`, `frontend/src/`
