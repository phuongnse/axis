# Detail Sections Evidence

> **Navigation**: [docs/foundations/data-display/detail-sections.md](./detail-sections.md) · [docs/foundations/data-display/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/ManagedDialogTabs.tsx`, `frontend/tests/managed-dialog-tabs.test.tsx`, `frontend/tests/rules-page.test.tsx`, `frontend/tests/business-objects-page.test.tsx` | `python scripts/axis.py frontend test tests/managed-dialog-tabs.test.tsx tests/rules-page.test.tsx tests/business-objects-page.test.tsx` |
| AT-002 | `frontend/e2e/manage-rules.pw.ts`, `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/manage-rules.pw.ts -g "rule catalog exposes inputs and pure system details"`; `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "AT-013 browser journey creates, saves, and publishes a definition"` |
