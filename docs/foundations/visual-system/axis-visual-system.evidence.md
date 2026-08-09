# Axis Visual System Evidence

> **Navigation**: [docs/foundations/visual-system/axis-visual-system.md](./axis-visual-system.md) · [docs/foundations/visual-system/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `scripts/axis_frontend_policy.py`, `scripts/tests/test_frontend_policy.py`, `frontend/ui-foundation.json`, `frontend/ui-baseline.json` | `python scripts/axis.py check policy-tests --test scripts.tests.test_frontend_policy.TestFrontendUiSystemPolicy`; `python scripts/axis.py check frontend-quality`; `python scripts/axis.py check ui-foundation`; `python scripts/axis.py check ui-baseline`; `python scripts/axis.py frontend ci` |
| AT-002 | `frontend/tests/async-button.test.tsx`, `frontend/tests/async-content.test.tsx`, `frontend/tests/option-list.test.tsx`, `frontend/tests/page-layout.test.tsx`, `frontend/tests/resource-workspace.test.tsx`, `frontend/tests/ui-primitives.test.tsx`, `frontend/src/hooks/usePendingVisibility.test.tsx` | `python scripts/axis.py frontend test tests/async-button.test.tsx tests/async-content.test.tsx tests/option-list.test.tsx tests/page-layout.test.tsx tests/resource-workspace.test.tsx tests/ui-primitives.test.tsx src/hooks/usePendingVisibility.test.tsx` |
| AT-003 | `frontend/tests/business-objects-page.test.tsx`, `frontend/tests/resource-workspace.test.tsx`, `frontend/tests/data-table.test.tsx` | `python scripts/axis.py frontend test tests/business-objects-page.test.tsx tests/resource-workspace.test.tsx tests/data-table.test.tsx` |
| AT-004 | `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "AT-004 golden reference visual matrix"` |
| AT-005 | `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "AT-005 golden reference integrates independent managed definition windows"` |
| AT-006 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/register-page.test.tsx`, `frontend/tests/verify-email-page.test.tsx`, `frontend/src/features/memberships/components/AcceptWorkspaceInvitationPage.test.tsx`, `frontend/src/features/memberships/components/MembershipManagementPage.test.tsx`, `frontend/src/features/product-roles/components/ProductRoleAssignmentsPage.test.tsx`, `frontend/src/features/service-identities/components/ServiceIdentitiesPage.test.tsx`, `frontend/tests/rules-page.test.tsx`, `frontend/src/features/solutions/components/SolutionsPage.test.tsx` | `python scripts/axis.py frontend test tests/sign-in-page.test.tsx tests/register-page.test.tsx tests/verify-email-page.test.tsx src/features/memberships/components/AcceptWorkspaceInvitationPage.test.tsx src/features/memberships/components/MembershipManagementPage.test.tsx src/features/product-roles/components/ProductRoleAssignmentsPage.test.tsx src/features/service-identities/components/ServiceIdentitiesPage.test.tsx tests/rules-page.test.tsx src/features/solutions/components/SolutionsPage.test.tsx` |

## Current verification

- The light/dark desktop/compact golden matrix was explicitly accepted on 2026-08-10, after the retained Playwright screenshots were reviewed.
- The frozen gate applies page anatomy, async ownership, query-state semantics, interaction-state ownership, and persistent option selection across the complete frontend source tree.
- Focused consumer suites and frontend compile/lint pass after clean cutover; background refresh keeps current content and feature code contains no raw pending animation or local timing implementation.
