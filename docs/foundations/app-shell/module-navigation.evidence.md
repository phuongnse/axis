# Module Navigation Evidence

> **Navigation**: [docs/foundations/app-shell/module-navigation.md](./module-navigation.md) · [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test tests/app-shell.test.tsx` |
| AT-002, AT-003 | `frontend/tests/module-navigation.test.tsx` | `python scripts/axis.py frontend script test tests/module-navigation.test.tsx` |
| AT-004 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py frontend script test:e2e e2e/app-frame.pw.ts` |
| AT-005 | `frontend/src/lib/module-navigation.ts`, `frontend/src/lib/module-navigation-registry.ts`, `frontend/src/components/shared/ModuleNavigation.tsx`, `frontend/src/features/preferences/translations.ts` | `python scripts/axis.py frontend ci` |
