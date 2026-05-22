## Summary

<!-- 2–4 sentences: what this PR does overall and why -->

## Commits

<!-- Add one row per commit (newest last). Do not remove earlier rows when you push. -->

| Commit | Description |
|--------|-------------|
| | |

## Requirements & rules followed

<!-- Check what applies. Mark N/A with a short reason. See CONTRIBUTING.md and docs/playbooks/agent-checklist.md for detail. -->

- [ ] **CI** — required jobs green on this PR (`.NET`, `Frontend`, **Doc drift** when applicable)
- [ ] **Gate 1** — `dotnet build` + `dotnet test` (full solution) when `src/` or `tests/` changed
- [ ] **Gate 1** — `dotnet format --verify-no-changes` when `src/` or `tests/` changed
- [ ] **Gate 1** — `npm run ci` + `npm run test` when `frontend/` changed
- [ ] **Doc drift** — `./scripts/check-doc-drift.sh` before push when `src/`, `tests/`, or `docs/epics/` changed; CI **Doc drift** green
- [ ] **Docs same PR** — epic/feature callouts, epic README, or `PROGRESS.md` updated when implementation status changed
- [ ] **Spec → code** — changes match feature file ACs (or gaps documented in callouts)
- [ ] **Gate 3** — no undocumented spec gaps; patterns/TECH_STACK/CLAUDE updated if a new rule applied
