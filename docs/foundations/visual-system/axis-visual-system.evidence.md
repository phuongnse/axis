# Axis UI Constitution Evidence

> **Navigation**: [docs/foundations/visual-system/axis-visual-system.md](./axis-visual-system.md) · [docs/foundations/visual-system/README.md](./README.md) · [docs/foundations/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `theme/axis-theme.json`, `scripts/axis_theme.py`, `scripts/tests/test_axis_theme.py`, `frontend/src/theme.generated.css`, `frontend/src/theme.generated.ts` | `python scripts/axis.py check theme`; `python scripts/axis.py check policy-tests --test scripts.tests.test_axis_theme.TestAxisTheme`; `python scripts/axis.py frontend ci` |
| AT-002 | `scripts/axis_frontend_policy.py`, `scripts/tests/test_frontend_policy.py`, `frontend/ui-foundation.json` | `python scripts/axis.py check policy-tests --test scripts.tests.test_frontend_policy.TestFrontendUiSystemPolicy`; `python scripts/axis.py check policy-tests --test scripts.tests.test_frontend_policy.TestUiFoundationPhase`; `python scripts/axis.py check frontend-quality`; `python scripts/axis.py check ui-foundation`; `python scripts/axis.py frontend ci` |
| AT-003 | `frontend/tests/async-button.test.tsx`, `frontend/tests/async-content.test.tsx`, `frontend/tests/option-list.test.tsx`, `frontend/tests/page-layout.test.tsx`, `frontend/tests/resource-workspace.test.tsx`, `frontend/tests/ui-primitives.test.tsx`, `frontend/tests/data-table.test.tsx`, `frontend/src/hooks/usePendingVisibility.test.tsx` | `python scripts/axis.py frontend test tests/async-button.test.tsx tests/async-content.test.tsx tests/option-list.test.tsx tests/page-layout.test.tsx tests/resource-workspace.test.tsx tests/ui-primitives.test.tsx tests/data-table.test.tsx src/hooks/usePendingVisibility.test.tsx` |
| AT-004 | `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "AT-004 golden reference visual matrix"` |
| AT-005 | `frontend/e2e/define-business-object.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/define-business-object.pw.ts -g "AT-005 golden reference integrates independent managed definition windows"` |
| AT-006 | `frontend/tests/business-objects-page.test.tsx`, `frontend/src/features/memberships/components/MembershipManagementPage.test.tsx`, `frontend/src/features/product-roles/components/ProductRoleAssignmentsPage.test.tsx`, `frontend/src/features/service-identities/components/ServiceIdentitiesPage.test.tsx`, `frontend/tests/rules-page.test.tsx`, `frontend/src/features/solutions/components/SolutionsPage.test.tsx` | `python scripts/axis.py frontend test tests/business-objects-page.test.tsx src/features/memberships/components/MembershipManagementPage.test.tsx src/features/product-roles/components/ProductRoleAssignmentsPage.test.tsx src/features/service-identities/components/ServiceIdentitiesPage.test.tsx tests/rules-page.test.tsx src/features/solutions/components/SolutionsPage.test.tsx`; `python scripts/axis.py frontend ci` |

## Current verification

- The replacement constitution and semantic theme are `reference-ready`. The Account clean cutover conforms to the same semantic roles, and the final focused browser run passed App Frame AT-002/AT-003 plus visual-system AT-004/AT-005 against the rebuilt web service.
- The browser proof covered desktop and compact layout, stable Account geometry, both Workspace-switch directions, delayed in-row pending feedback, no document/menu scrollbar flash, light/dark golden visuals, reduced motion, and independent managed windows without publishing, installing, or resetting local data.
- `accepted` still requires explicit user visual acceptance of the running reference. `adopted` additionally requires complete active-surface conformance evidence; neither state is claimed.
