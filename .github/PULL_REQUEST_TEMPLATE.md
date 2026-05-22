## Summary

<!-- 2–4 sentences: what this PR does overall and why -->

## Requirements & rules followed

<!-- Most important first. Check what applies; mark N/A with a short reason. Commits: see the PR Commits tab. -->

- [ ] **Spec → code** — changes match feature file ACs (or gaps documented in callouts)
- [ ] **CI** — required jobs green (`.NET`, `Frontend`, **Doc drift** when applicable)
- [ ] **Docs same PR** — `docs/epics/`, feature callouts, epic README, or `PROGRESS.md` updated when status changed
- [ ] **Doc drift** — `./scripts/check-doc-drift.sh` before push when `src/`, `tests/`, or `docs/epics/` changed; CI **Doc drift** green
- [ ] **Gate 1** — `dotnet build` + `dotnet test` (full solution) when `src/` or `tests/` changed
- [ ] **Gate 1** — `dotnet format --verify-no-changes` when `src/` or `tests/` changed
- [ ] **Gate 1** — `npm run ci` + `npm run test` when `frontend/` changed
- [ ] **Gate 3** — no undocumented spec gaps; `patterns.md` / `TECH_STACK.md` / `CLAUDE.md` updated if a new rule applied
