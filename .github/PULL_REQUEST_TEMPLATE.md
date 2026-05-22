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
```

## Gate 2a — Doc drift (required)

```text
Gate 2a:
- ./scripts/check-doc-drift.sh → ran (green) / not triggered (reason)
```

## Gate 2b — Docs walk-through (required)

```text
Gate 2b:
- Library → TECH_STACK.md / not triggered
- New pattern → patterns.md or patterns-index.md / not triggered
- US layer callout → docs/epics/…/features/… / not triggered
- Epic README + PROGRESS → … / not triggered
- Wireframe/diagram SVG regenerated → … / not triggered
- (other Gate 2 rows from agent-checklist) →
```

## Gate 3 — Retrospective (required)

```text
Gate 3:
1. New rule from test failure?
2. Invented invariant without AC?
3. Infrastructure footgun?
4. Non-obvious test setup?
5. Changed direction mid-task?
6. Spec gap discovered?
7. Incident-level detail in rule text?
```

## Verification

- [ ] `./scripts/check-doc-drift.sh` (CI job **Doc drift**)
- [ ] `dotnet build` + `dotnet test` (if `src/` or `tests/`) — full solution, not a filter
- [ ] `npm run ci` + `npm run test` (if `frontend/`)
