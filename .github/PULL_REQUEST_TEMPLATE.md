## Summary

<!-- What changed and why (2–4 sentences) -->

## AC map

<!-- Required for feature work -->

| AC / US | Layer | Implementation |
|---------|-------|------------------|
| | | |

## Gate 2 — Docs (required)

```
Gate 2:
-
```

## Gate 3 — Retrospective (required)

```
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

- [ ] `./scripts/check-doc-drift.sh`
- [ ] `dotnet build` + `dotnet test unit-tests.slnf` (if `src/` or `tests/`)
- [ ] `npm run ci` + `npm run test` (if `frontend/`)
