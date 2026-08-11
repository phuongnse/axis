# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md) · [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/AuthenticatedFrame.tsx`, `frontend/tests/app-shell.test.tsx`, `frontend/tests/surface-contracts.test.tsx`, `frontend/src/features/workspaces/WorkspaceControl.test.tsx`, `frontend/tests/option-list.test.tsx` | `python scripts/axis.py frontend test tests/app-shell.test.tsx tests/surface-contracts.test.tsx src/features/workspaces/WorkspaceControl.test.tsx tests/option-list.test.tsx` |
| AT-002 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
| AT-003 | `frontend/src/components/shared/AccountAvatar.tsx`, `frontend/src/components/shared/AccountSurface.tsx`, `frontend/tests/account-surface.test.tsx`, `frontend/src/components/shared/AppFooter.tsx`, `frontend/src/components/shared/AppHeader.tsx`, `frontend/src/components/shared/AppShell.tsx`, `frontend/src/components/shared/AuthenticatedFrame.tsx`, `frontend/src/components/shared/OptionList.tsx`, `frontend/src/features/preferences/translations.ts`, `frontend/src/features/workspaces/WorkspaceControl.tsx` | `python scripts/axis.py frontend ci`; `python scripts/axis.py frontend test tests/account-surface.test.tsx tests/surface-contracts.test.tsx` |
