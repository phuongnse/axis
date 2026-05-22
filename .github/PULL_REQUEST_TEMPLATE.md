## Summary

<!-- What changed and why (2–4 sentences) -->

## Gate 0 — Ready (required when `src/`, `tests/`, or `frontend/` change)

<!-- AC map: no blank cells. Gap sweep before API: grep "Application: ⚠️|Infrastructure: ⚠️" docs/epics/ -->

| AC / US | Layer | File / test |
|---------|-------|-------------|
| | | |

**Docs touched:** <!-- list docs/epics paths -->

## Gate 1 — Build & tests (required)

Local commands must match CI. Use `not triggered` only when that path did not change.

```text
Gate 1:
- dotnet build → ran / not triggered (reason)
- dotnet test (full solution, same as CI) → ran / not triggered (reason)
- dotnet format --verify-no-changes → ran / not triggered (reason)
- TODO/FIXME/placeholder/stub grep (empty) → ran / not triggered (reason)
- npm run ci → ran / not triggered (reason)
- npm run test → ran / not triggered (reason)
- ./scripts/check-doc-drift.sh → ran / not triggered (reason)
```

## Gate 2 — Docs (required)

Run `./scripts/check-doc-drift.sh` before push when code or `docs/epics/` change (also in Gate 1). CI job **Doc drift** must be green — do not paste script output here. **Paste Gate 2** walk-through:

```text
Gate 2:
- Library → TECH_STACK.md / not triggered
- New pattern → patterns.md or patterns-index.md / not triggered
- US layer callout → docs/epics/…/features/… / not triggered
- Epic README + PROGRESS → … / not triggered
- Wireframe/diagram SVG regenerated → … / not triggered
- (other rows from agent-checklist) →
```

## Gate 3 — Retrospective (required)

Answer **Yes** or **No** on each line — do not write only `1–7 No`. If **Yes**, note which doc you updated in this PR.

```text
Gate 3:
1. New rule from test failure? → No / Yes →
2. Invented invariant without AC? → No / Yes →
3. Infrastructure footgun? → No / Yes →
4. Non-obvious test setup? → No / Yes →
5. Changed direction mid-task? → No / Yes →
6. Spec gap discovered? → No / Yes →
7. Incident-level detail in rule text? → No / Yes →
```

## CI checks

GitHub shows pass/fail for build, test, and **Doc drift** — no need to duplicate in Gate blocks above.

- [ ] `./scripts/check-doc-drift.sh` locally before push (when `src/`, `tests/`, or `docs/epics/` changed)
- [ ] All required CI checks green (including **Doc drift** when code changed)
- [ ] `dotnet build` + `dotnet test` locally if you touched `src/` or `tests/` (full solution)
- [ ] `npm run ci` + `npm run test` locally if you touched `frontend/`
