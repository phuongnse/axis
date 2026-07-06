# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md) · [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test tests/app-shell.test.tsx` |
| AT-002 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
| AT-003 | `frontend/src/components/shared/AccountAvatar.tsx`, `frontend/src/components/shared/AppActionsMenu.tsx`, `frontend/src/components/shared/AppFooter.tsx`, `frontend/src/components/shared/AppHeader.tsx`, `frontend/src/components/shared/AppShell.tsx`, `frontend/src/features/preferences/translations.ts` | `python scripts/axis.py frontend ci` |
