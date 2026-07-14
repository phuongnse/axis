# Data Table Evidence

> **Navigation**: [docs/foundations/data-display/data-table.md](./data-table.md) · [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002, AT-003, AT-004 | `frontend/tests/data-table.test.tsx` | `python scripts/axis.py frontend script test -- data-table.test.tsx` |
| AT-005 | `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- rules-page.test.tsx` |
| AT-006 | `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts` |
| AT-007 | `frontend/src/components/shared/data-table/DataTable.tsx`, `frontend/src/components/shared/data-table/DataTableToolbar.tsx`, `frontend/src/components/shared/data-table/filtering.ts`, `frontend/src/components/shared/data-table/types.ts`, `frontend/src/components/ui/table.tsx`, `frontend/src/features/rules/components/RulesPage.tsx`, `frontend/src/features/preferences/translations.ts` | `python scripts/axis.py frontend ci` |
