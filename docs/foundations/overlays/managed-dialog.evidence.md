# Managed Dialog Evidence

> **Navigation**: [docs/foundations/overlays/managed-dialog.md](./managed-dialog.md) · [docs/foundations/overlays/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/app-shell.test.tsx`, `frontend/tests/rules-page.test.tsx`, `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py frontend script test -- --run tests/app-shell.test.tsx tests/rules-page.test.tsx`; `python scripts/axis.py frontend script test:e2e -- e2e/manage-rules.pw.ts` |
| AT-002 | `frontend/tests/rules-page.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/rules-page.test.tsx` |
| AT-003 | `frontend/tests/rules-page.test.tsx`, `frontend/tests/business-objects-page.test.tsx`, `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test -- --run tests/rules-page.test.tsx tests/business-objects-page.test.tsx tests/app-shell.test.tsx` |
| AT-004 | `frontend/e2e/manage-rules.pw.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/manage-rules.pw.ts` |
| AT-005 | `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/define-business-object.pw.ts` |
| AT-006 | `frontend/src/components/shared/ManagedDialog.tsx`, `frontend/src/components/shared/ManagedWindowManager.tsx`, `frontend/src/components/shared/ManagedWindowHost.tsx`, `frontend/src/lib/managed-window-registry.ts`, `frontend/ui-baseline.json` | `python scripts/axis.py frontend ci`; `python scripts/axis.py check ui-baseline` |
