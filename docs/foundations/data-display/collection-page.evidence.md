# Collection Page Evidence

> **Navigation**: [docs/foundations/data-display/collection-page.md](./collection-page.md) · [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/data-table/DataTable.tsx`, `frontend/tests/data-table.test.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- tests/data-table.test.tsx tests/rules-page.test.tsx` |
| AT-002 | `frontend/src/features/business-objects/components/BusinessObjectsPage.tsx`, `frontend/src/features/rules/components/RulesPage.tsx`, `frontend/tests/business-objects-page.test.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- tests/business-objects-page.test.tsx tests/rules-page.test.tsx` |
| AT-003 | `frontend/src/features/business-objects/components/BusinessObjectDefinitionDialog.tsx`, `frontend/src/features/rules/components/RuleEditorDialog.tsx`, `frontend/tests/business-objects-page.test.tsx`, `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- tests/business-objects-page.test.tsx tests/rules-page.test.tsx` |
| AT-004 | `frontend/e2e/define-business-object.pw.ts`, `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts e2e/manage-rules.pw.ts` |
| AT-005 | `frontend/src/components/shared/data-table/types.ts`, `frontend/src/components/ui/dialog.tsx`, `frontend/src/routes/_authenticated/business-objects.tsx`, `frontend/src/routes/_authenticated/rules.tsx` | `python scripts/axis.py frontend ci` |
