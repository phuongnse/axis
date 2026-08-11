# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md) · [docs/foundations/app-shell/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/AuthenticatedFrame.tsx`, `frontend/src/components/shared/AccountSurface.tsx`, `frontend/src/features/preferences/LanguageControl.tsx`, `frontend/src/features/preferences/ThemeControl.tsx`, `frontend/tests/app-shell.test.tsx`, `frontend/tests/account-surface.test.tsx`, `frontend/tests/surface-contracts.test.tsx`, `frontend/src/features/workspaces/WorkspaceControl.test.tsx`, `frontend/tests/option-list.test.tsx` | `python scripts/axis.py frontend test tests/app-shell.test.tsx tests/account-surface.test.tsx tests/surface-contracts.test.tsx src/features/workspaces/WorkspaceControl.test.tsx tests/option-list.test.tsx` |
| AT-002 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
| AT-003 | `frontend/src/components/shared/AccountAvatar.tsx`, `frontend/src/components/shared/AccountSurface.tsx`, `frontend/tests/account-surface.test.tsx`, `frontend/src/components/shared/AppFooter.tsx`, `frontend/src/components/shared/AppHeader.tsx`, `frontend/src/components/shared/AppShell.tsx`, `frontend/src/components/shared/AuthenticatedFrame.tsx`, `frontend/src/components/shared/OptionList.tsx`, `frontend/src/features/preferences/translations.ts`, `frontend/src/features/workspaces/WorkspaceControl.tsx` | `python scripts/axis.py frontend ci`; `python scripts/axis.py frontend test tests/account-surface.test.tsx tests/surface-contracts.test.tsx` |

## Current verification

- The current Reference Product deployment owns the recorded Compose topology. Its documented trusted Axis-E2E wrapper ran the canonical Axis browser service and passed all 5 App Frame browser tests without snapshot updates.
- Held-response Account journeys prove that Workspace and preference actions expose semantic busy state in the same click turn, retain stable geometry through completion, and return to rest before visual comparison.
- Account accepts typed Workspace and preference state instead of feature-rendered subsystem slots. Browser evidence compares all four semantic regions for one symmetric inset and verifies the Create Organization resting boundary before visual capture.
- AT-002 includes a compact-height Account journey with 12 eligible organization Workspaces; keyboard focus reaches sign-out, the focused action remains in view, and the document does not become a scroll owner.
- The eight Account baselines were regenerated through the bounded Axis snapshot output, inspected across light/dark × desktop/compact × EN/VI, then verified by the comparison run above.
- The project owner explicitly accepted the rendered Account reference on 2026-08-11 after direct inspection. This acceptance is scoped to Account and does not claim perceptual adoption for other active surface contracts.
- The typed active-surface registry has one Account consumer. Page actions, bounded confirmation actions, and inline prompt actions have different relationships and owners, so the standalone section-action presentation remains local to Account until another representative consumer proves the same semantic role.
