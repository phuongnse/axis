## Summary

<!-- 2–4 sentences: what this PR does overall and why -->

## Requirements & rules followed

<!-- Most important first. Check what applies; mark N/A with a short reason. CI status: PR Checks tab (includes Doc drift). -->

- [ ] **Spec → code** — changes match feature file ACs (or gaps documented in callouts)
- [ ] **Gate 0** — AC map complete; epic/feature docs identified (when shipping code)
- [ ] **CI** — required PR checks green (`.NET`, `Frontend`, **Doc drift** when applicable)
- [ ] **Gate 2** — docs updated in same PR (callouts, epic README, `PROGRESS.md`, `TECH_STACK` / patterns as triggered)
- [ ] **Gate 1** — `dotnet build` + `dotnet test` (full solution), `dotnet format`, and/or `npm run ci` + `npm run test` for paths you changed (N/A with reason)
- [ ] **Gate 3** — retrospective done; `patterns.md` / feature file / `TECH_STACK.md` / `CLAUDE.md` updated if needed
