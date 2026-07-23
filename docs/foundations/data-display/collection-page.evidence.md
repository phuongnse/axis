# Collection Page Evidence

> **Navigation**: [docs/foundations/data-display/collection-page.md](./collection-page.md) · [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/data-table/DataTable.tsx`, `frontend/tests/data-table.test.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/data-table.test.tsx tests/rules-page.test.tsx` |
| AT-002 | `frontend/tests/rules-page.test.tsx`, `frontend/tests/business-objects-page.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/rules-page.test.tsx tests/business-objects-page.test.tsx` |
| AT-003 | `frontend/tests/rules-page.test.tsx`, `frontend/tests/business-objects-page.test.tsx`, `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/rules-page.test.tsx tests/business-objects-page.test.tsx tests/app-shell.test.tsx` |
| AT-004 | `frontend/e2e/manage-rules.pw.ts`, `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts e2e/define-business-object.pw.ts` |
| AT-005 | `frontend/src/components/shared/data-table/DataTable.tsx`, `frontend/src/components/shared/ManagedWindowManager.tsx`, `frontend/src/lib/managed-window-registry.ts` | `python scripts/axis.py frontend ci`; `python scripts/axis.py check ui-baseline` |
