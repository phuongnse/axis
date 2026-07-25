# Search Experience Evidence

> **Navigation**: [docs/foundations/search/search-experience.md](./search-experience.md) · [docs/foundations/search/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `frontend/src/components/shared/data-table/DataTable.tsx`, `frontend/tests/data-table.test.tsx`, `frontend/tests/rules-page.test.tsx`, `frontend/tests/business-objects-page.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/data-table.test.tsx tests/rules-page.test.tsx tests/business-objects-page.test.tsx` |
| AT-003 | `frontend/src/features/rules/components/RuleExpressionGuide.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/rules-page.test.tsx` |
| AT-004 | `frontend/e2e/manage-rules.pw.ts`, `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts -g "rules catalog search"`; `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts -g "read-only rule details"`; `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts -g "workspace rule authoring"`; `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "catalog search"` |
| AT-005 | `scripts/axis_frontend_policy.py`, `scripts/tests/test_frontend_policy.py`, `frontend/src/features/rules/components/RulesPage.tsx`, `frontend/src/features/business-objects/components/BusinessObjectsPage.tsx`, `frontend/src/features/rules/components/RuleExpressionGuide.tsx` | `python scripts/axis.py check frontend-quality`; `python scripts/axis.py check policy-tests`; `python scripts/axis.py frontend ci` |
