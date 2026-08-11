"""Regression tests for deterministic frontend source-policy checks."""

from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis  # noqa: E402


class TestFrontendE2eStructure(unittest.TestCase):
    def issues_for_frontend(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_e2e_structure_issues(root=root)

    def test_rejects_fixed_sleep_timeout_override_and_serial_state(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/e2e/example.pw.ts": (
                    "test.describe.configure({ mode: 'serial' });\n"
                    "test('example', async ({ page }, testInfo) => {\n"
                    "  testInfo.setTimeout(120_000);\n"
                    "  await page.waitForTimeout(250);\n"
                    "});\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("independently runnable", joined)
        self.assertIn("centrally owned Playwright timeout", joined)
        self.assertIn("fixed browser sleeps", joined)

    def test_accepts_observable_independent_journey(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/e2e/example.pw.ts": (
                    "test('example', async ({ page }) => {\n"
                    "  await page.goto('/rules');\n"
                    "  await expect(page).toHaveURL(/rules/);\n"
                    "});\n"
                )
            }
        )

        self.assertEqual([], issues)


class TestFrontendComponentFileNames(unittest.TestCase):
    def issues_for_frontend(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_component_file_name_issues(root=root)

    def test_rejects_styled_route_markup(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "export function RoutePage() {\n"
                    "  return <main className=\"p-4\">Bad</main>;\n"
                    "}\n"
                )
            }
        )

        self.assertIn("route files compose page components only", "\n".join(issues))

    def test_accepts_route_component_composition(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing/components/LandingPage';\n"
                    "export const Route = { component: LandingPage };\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_rejects_non_registry_case_ui_primitive_filename(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/CustomControl.tsx": "export function CustomControl() { return null; }\n",
            }
        )

        self.assertIn("shadcn UI primitive files must use registry kebab-case names", "\n".join(issues))

    def test_rejects_non_pascal_case_shared_component_filename(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/example-control.tsx": "export function ExampleControl() { return null; }\n",
                "frontend/src/components/shared/layout-state.ts": "export const layoutState = {};\n",
            }
        )

        joined = "\n".join(issues)
        self.assertIn("shared React component files must use PascalCase names", joined)
        self.assertIn("shared non-component modules must use camelCase names", joined)

    def test_accepts_shared_component_filename_conventions(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/ExampleControl.tsx": "export function ExampleControl() { return null; }\n",
                "frontend/src/components/shared/layoutState.ts": "export const layoutState = {};\n",
            }
        )

        self.assertEqual([], issues)

    def test_rejects_native_standard_controls_outside_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/CustomForm.tsx": (
                    "export function CustomForm() {\n"
                    "  return <button type=\"button\"><input /></button>;\n"
                    "}\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("standard UI control <button> must use a shared shadcn UI primitive", joined)
        self.assertIn("standard UI control <input> must use a shared shadcn UI primitive", joined)

    def test_rejects_raw_structured_controls_with_shared_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleTable.tsx": (
                    "export function RuleTable() {\n"
                    "  return <dialog><progress /><table><tbody><tr><td>Rule</td></tr></tbody></table></dialog>;\n"
                    "}\n"
                )
            }
        )

        joined = "\n".join(issues)
        for element in ("dialog", "progress", "table", "tbody", "tr", "td"):
            self.assertIn(
                f"standard UI control <{element}> must use a shared shadcn UI primitive",
                joined,
            )

    def test_rejects_headless_ui_import_outside_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/CustomMenu.tsx": (
                    "import { Menu } from '@base-ui/react/menu';\n"
                    "export function CustomMenu() { return null; }\n"
                )
            }
        )

        self.assertIn("headless UI primitives belong in shadcn primitives", "\n".join(issues))

    def test_rejects_radix_ui_import_outside_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/CustomMenu.tsx": (
                    "import * as Menu from '@radix-ui/react-dropdown-menu';\n"
                    "export function CustomMenu() { return null; }\n"
                )
            }
        )

        self.assertIn("headless UI primitives belong in shadcn primitives", "\n".join(issues))

    def test_rejects_native_fallback_import_in_product_code(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleFilter.tsx": (
                    "import { NativeSelect } from '@/components/ui/native-select';\n"
                    "export function RuleFilter() { return <NativeSelect />; }\n"
                )
            }
        )

        self.assertIn(
            "native fallback primitives require an approved platform-native behavior exception",
            "\n".join(issues),
        )

    def test_rejects_unformatted_select_value_in_product_code(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleFilter.tsx": (
                    "import { SelectValue } from '@/components/ui/select';\n"
                    "export function RuleFilter() { return <SelectValue placeholder=\"All origins\" />; }\n"
                )
            }
        )

        self.assertIn(
            "SelectValue must format the selected value from the same display-label source as SelectItem",
            "\n".join(issues),
        )

    def test_accepts_select_value_with_display_label_formatter(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleFilter.tsx": (
                    "import { SelectValue } from '@/components/ui/select';\n"
                    "export function RuleFilter() {\n"
                    "  return <SelectValue>{(value) => value === 'All' ? 'All origins' : value}</SelectValue>;\n"
                    "}\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_accepts_native_standard_controls_inside_shadcn_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/button.tsx": (
                    "export function Button() {\n"
                    "  return <button type=\"button\"><input /></button>;\n"
                    "}\n"
                ),
            }
        )

        self.assertEqual([], issues)


class TestFrontendUiSystemPolicy(unittest.TestCase):
    def issues_for_frontend(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_ui_system_issues(root=root)

    def test_rejects_app_dependency_from_registry_primitive(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/button.tsx": (
                    "import { RulesPage } from '@/features/rules/components/RulesPage';\n"
                )
            }
        )

        self.assertIn("registry primitives cannot depend on feature", "\n".join(issues))

    def test_rejects_feature_local_pending_visuals_and_background_refresh_loading(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RulePanel.tsx": (
                    "import { Spinner } from '@/components/ui/spinner';\n"
                    "export const definition = { loading: query.isFetching };\n"
                    "export function RulePanel() { return <Spinner className=\"animate-spin\" />; }\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("must use shared async-state patterns", joined)
        self.assertIn("background isFetching preserves current content", joined)

    def test_rejects_legacy_query_loading_raw_status_and_pending_label_swap(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RulePanel.tsx": (
                    "export function RulePanel({ loading, query }) {\n"
                    "  const content = query.isLoading ? <p role='status'>Loading</p> : null;\n"
                    "  return <><Button>{loading ? t('saving') : t('save')}</Button>{content}</>;\n"
                    "}\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("initial state must use isPending", joined)
        self.assertIn("semantic shared async region", joined)
        self.assertIn("stable visible label", joined)

    def test_rejects_shared_pending_internals_from_feature_code(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RulePanel.tsx": (
                    "import { PendingIndicator } from '@/components/shared/PendingIndicator';\n"
                    "import { usePendingVisibility } from '@/hooks/usePendingVisibility';\n"
                    "export function RulePanel() { return <PendingIndicator>Loading</PendingIndicator>; }\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("PendingIndicator is an internal shared visual", joined)
        self.assertIn("pending timing is owned by shared async patterns", joined)

    def test_accepts_semantic_async_content_without_feature_timing(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RulePanel.tsx": (
                    "import { AsyncContent } from '@/components/shared/AsyncContent';\n"
                    "export function RulePanel() {\n"
                    "  return <AsyncContent pending pendingLabel='Loading'>Ready</AsyncContent>;\n"
                    "}\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_accepts_shared_pending_patterns_and_initial_query_loading(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/AsyncButton.tsx": (
                    "import { Spinner } from '@/components/ui/spinner';\n"
                    "export function AsyncButton() { return <Spinner className=\"animate-spin\" />; }\n"
                ),
                "frontend/src/features/rules/components/RulePanel.tsx": (
                    "import { AsyncButton } from '@/components/shared/AsyncButton';\n"
                    "export const definition = { loading: query.isPending };\n"
                    "export function RulePanel() { return <AsyncButton />; }\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_rejects_relative_app_dependency_from_registry_primitive(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/button.tsx": (
                    "import { RulesPage } from '../../features/rules/components/RulesPage';\n"
                )
            }
        )

        self.assertIn("registry primitives cannot depend on feature", "\n".join(issues))

    def test_rejects_dynamic_and_commonjs_app_dependencies_from_registry_primitive(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/button.tsx": (
                    "const feature = import('@/features/rules/components/RulesPage');\n"
                    "const shared = import(\n"
                    "  '../../components/shared/RuleStatus'\n"
                    ");\n"
                    "const route = require('@/routes/__root');\n"
                )
            }
        )

        self.assertEqual(3, len(issues))
        self.assertTrue(all("registry primitives cannot depend on feature" in issue for issue in issues))


    def test_rejects_palette_arbitrary_value_and_inline_color_outside_upstream_zone(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleStatus.tsx": (
                    "export const classes = 'bg-red-500 text-white size-[1.625rem]';\n"
                    "export const style = { color: '#fff' };\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("hard-coded Tailwind palette utility `bg-red-500`", joined)
        self.assertIn("hard-coded Tailwind palette utility `text-white`", joined)
        self.assertIn("arbitrary Tailwind value `size-[1.625rem]`", joined)
        self.assertIn("component-local hard-coded color", joined)

    def test_rejects_raw_badge_import_outside_semantic_owners(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleStep.tsx": (
                    "import { Badge } from '@/components/ui/badge';\n"
                    "export function RuleStep() { return <Badge>When</Badge>; }\n"
                )
            }
        )

        self.assertIn(
            "raw Badge is restricted to semantic badge owners",
            "\n".join(issues),
        )

    def test_rejects_raw_alert_import_outside_status_notice_owner(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleFeedback.tsx": (
                    "import { Alert } from '@/components/ui/alert';\n"
                    "export function RuleFeedback() { return <Alert>Failed</Alert>; }\n"
                )
            }
        )

        self.assertIn(
            "raw Alert is restricted to the StatusNotice owner",
            "\n".join(issues),
        )

    def test_accepts_raw_badge_import_in_semantic_owners(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/StatusBadge.tsx": (
                    "import { Badge } from '@/components/ui/badge';\n"
                ),
                "frontend/src/components/shared/MetadataTag.tsx": (
                    "import { Badge } from '@/components/ui/badge';\n"
                ),
                "frontend/src/components/shared/data-table/DataTableToolbar.tsx": (
                    "import { Badge } from '@/components/ui/badge';\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_accepts_semantic_tokens_and_standard_tailwind_scale(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/RuleStatus.tsx": (
                    "export const classes = 'grid grid-cols-4 gap-2 bg-card text-foreground';\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_rejects_feature_owned_interaction_state_visuals(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/rules/components/RuleOption.tsx": (
                    "export const classes = "
                    "'hover:bg-accent aria-pressed:text-secondary-foreground group-hover:bg-muted "
                    "md:hover:bg-accent group-hover/menu:text-foreground "
                    "data-open:hover:bg-secondary disabled:bg-muted "
                    "peer-checked:text-primary peer-disabled:border-input';\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("interaction-state visual `hover:bg-accent`", joined)
        self.assertIn(
            "interaction-state visual `aria-pressed:text-secondary-foreground`",
            joined,
        )
        self.assertIn("interaction-state visual `group-hover:bg-muted`", joined)
        self.assertIn("interaction-state visual `md:hover:bg-accent`", joined)
        self.assertIn("interaction-state visual `group-hover/menu:text-foreground`", joined)
        self.assertIn("interaction-state visual `data-open:hover:bg-secondary`", joined)
        self.assertIn("interaction-state visual `disabled:bg-muted`", joined)
        self.assertIn("interaction-state visual `peer-checked:text-primary`", joined)
        self.assertIn("interaction-state visual `peer-disabled:border-input`", joined)

    def test_rejects_interaction_state_visuals_outside_the_shared_owner(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/OptionList.tsx": (
                    "export const classes = 'hover:bg-accent aria-pressed:bg-secondary';\n"
                )
            }
        )

        self.assertIn("interaction-state visual `hover:bg-accent`", "\n".join(issues))

    def test_accepts_interaction_state_visuals_in_the_shared_owner(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/interactionStates.ts": (
                    "export const classes = 'hover:bg-accent aria-pressed:bg-secondary';\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_allows_registry_owned_implementation_details(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/example.tsx": (
                    "export const classes = 'bg-red-500 w-[--anchor-width]';\n"
                )
            }
        )

        self.assertEqual([], issues)


class TestUiFoundationContracts(unittest.TestCase):
    def issues_for_foundation(
        self,
        *,
        manifest_changes: dict[str, object] | None = None,
        files: dict[str, str] | None = None,
        missing_files: set[str] | None = None,
    ) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest_path = root / "frontend/ui-foundation.json"
            manifest_path.parent.mkdir(parents=True, exist_ok=True)
            manifest = {
                "schemaVersion": 5,
                "contracts": {
                    "resource-workspace": {
                        "state": "defined",
                        "spec": "docs/foundations/data-display/collection-page.md",
                        "evidence": {
                            "component": ["frontend/tests/resource-workspace.test.tsx"],
                            "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                            "perceptual": [],
                        },
                    }
                },
                "enforcedContracts": {},
            }
            manifest.update(manifest_changes or {})
            manifest_path.write_text(f"{json.dumps(manifest)}\n", encoding="utf-8")

            default_files = {
                "frontend/src/lib/ui-foundation.ts": "export {};\n",
                "frontend/src/lib/active-surface-registry.ts": "export {};\n",
                "docs/foundations/data-display/collection-page.md": "# Collection Page\n",
                "frontend/tests/resource-workspace.test.tsx": "export {};\n",
                "frontend/e2e/resource-workspace.pw.ts": "export {};\n",
                "frontend/src/features/rules/components/RulesPage.tsx": (
                    "import { ResourceWorkspace } from '@/components/shared/ResourceWorkspace';\n"
                    "export function RulesPage() { return <ResourceWorkspace />; }\n"
                ),
            }
            default_files.update(files or {})
            for relative_path, content in default_files.items():
                if relative_path in (missing_files or set()):
                    continue
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")

            return axis.ui_foundation_issues(root=root)

    def test_accepts_contract_and_owned_evidence(self) -> None:
        self.assertEqual([], self.issues_for_foundation())

    def test_rejects_unknown_schema(self) -> None:
        issues = self.issues_for_foundation(manifest_changes={"schemaVersion": 4})
        self.assertIn("`schemaVersion` must be 5", "\n".join(issues))

    def test_rejects_unknown_contract_state(self) -> None:
        contracts = {
            "resource-workspace": {
                "state": "accepted",
                "spec": "docs/foundations/data-display/collection-page.md",
                "evidence": {
                    "component": ["frontend/tests/resource-workspace.test.tsx"],
                    "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                    "perceptual": [],
                },
            }
        }
        issues = self.issues_for_foundation(manifest_changes={"contracts": contracts})
        self.assertIn("state` must be one of: defined, enforced, verified", "\n".join(issues))

    def test_rejects_advanced_state_without_perceptual_evidence(self) -> None:
        for state in ("verified", "enforced"):
            with self.subTest(state=state):
                contracts = {
                    "resource-workspace": {
                        "state": state,
                        "spec": "docs/foundations/data-display/collection-page.md",
                        "evidence": {
                            "component": ["frontend/tests/resource-workspace.test.tsx"],
                            "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                            "perceptual": [],
                        },
                    }
                }
                issues = self.issues_for_foundation(manifest_changes={"contracts": contracts})
                self.assertIn(
                    f"cannot be `{state}` without version-controlled perceptual evidence",
                    "\n".join(issues),
                )

    def test_accepts_enforced_contract_with_versioned_perceptual_evidence(self) -> None:
        snapshot = (
            "frontend/e2e/resource-workspace.pw.ts-snapshots/"
            "resource-workspace-light-desktop-en-chromium-linux.png"
        )
        contracts = {
            "resource-workspace": {
                "state": "enforced",
                "spec": "docs/foundations/data-display/collection-page.md",
                "evidence": {
                    "component": ["frontend/tests/resource-workspace.test.tsx"],
                    "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                    "perceptual": [snapshot],
                },
            }
        }
        self.assertEqual(
            [],
            self.issues_for_foundation(
                manifest_changes={
                    "contracts": contracts,
                    "enforcedContracts": {"resource-workspace": True},
                },
                files={snapshot: "snapshot"},
            ),
        )

    def test_rejects_enforced_registry_state_drift(self) -> None:
        enforced_contract = {
            "resource-workspace": {
                "state": "enforced",
                "spec": "docs/foundations/data-display/collection-page.md",
                "evidence": {
                    "component": ["frontend/tests/resource-workspace.test.tsx"],
                    "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                    "perceptual": [],
                },
            }
        }
        issues = self.issues_for_foundation(manifest_changes={"contracts": enforced_contract})
        self.assertIn("missing from `enforcedContracts`", "\n".join(issues))

        issues = self.issues_for_foundation(
            manifest_changes={"enforcedContracts": {"resource-workspace": True}}
        )
        self.assertIn("registered as enforced while its state is `defined`", "\n".join(issues))

    def test_rejects_missing_evidence_file(self) -> None:
        contracts = {
            "resource-workspace": {
                "state": "defined",
                "spec": "docs/foundations/data-display/collection-page.md",
                "evidence": {
                    "component": ["frontend/tests/missing.test.tsx"],
                    "browser": ["frontend/e2e/resource-workspace.pw.ts"],
                    "perceptual": [],
                },
            }
        }
        issues = self.issues_for_foundation(manifest_changes={"contracts": contracts})
        self.assertIn("does not exist: frontend/tests/missing.test.tsx", "\n".join(issues))

    def test_rejects_evidence_outside_owned_test_roots(self) -> None:
        contracts = {
            "resource-workspace": {
                "state": "defined",
                "spec": "docs/foundations/data-display/collection-page.md",
                "evidence": {
                    "component": ["frontend/src/components/shared/ResourceWorkspace.tsx"],
                    "browser": ["frontend/src/browser-proof.ts"],
                    "perceptual": [],
                },
            }
        }
        issues = self.issues_for_foundation(
            manifest_changes={"contracts": contracts},
            files={
                "frontend/src/browser-proof.ts": "export {};\n",
                "frontend/src/components/shared/ResourceWorkspace.tsx": "export {};\n",
            },
        )
        joined = "\n".join(issues)
        self.assertIn("component evidence must be a frontend .test.tsx file", joined)
        self.assertIn("browser evidence must be a frontend/e2e .pw.ts file", joined)

    def test_rejects_missing_typed_foundation_source(self) -> None:
        issues = self.issues_for_foundation(
            missing_files={"frontend/src/lib/active-surface-registry.ts"}
        )

        self.assertIn("typed UI foundation source is missing", "\n".join(issues))

    def test_rejects_legacy_path_based_surface_inventory(self) -> None:
        issues = self.issues_for_foundation(manifest_changes={"surfaces": []})

        self.assertIn("unexpected keys: surfaces", "\n".join(issues))


class TestUiBaseline(unittest.TestCase):
    def create_baseline(self, root: Path) -> None:
        files = {
            "frontend/components.json": '{"style":"base-nova"}\n',
            "frontend/src/index.css": '@import "tailwindcss";\n',
            "frontend/src/theme.generated.css": ":root {}\n",
            "frontend/src/components/ui/button.tsx": "export function Button() { return null; }\n",
        }
        for relative_path, content in files.items():
            path = root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            axis.write_ui_baseline(root)

    def test_accepts_unchanged_approved_ui_baseline(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)

            issues = axis.ui_baseline_issues(root)

        self.assertEqual([], issues)

    def test_rejects_non_object_components_config(self) -> None:
        for value in ("[]", "null", "1"):
            with self.subTest(value=value), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                config = root / "frontend/components.json"
                theme = root / "frontend/src/index.css"
                generated_theme = root / "frontend/src/theme.generated.css"
                config.parent.mkdir(parents=True)
                theme.parent.mkdir(parents=True)
                config.write_text(f"{value}\n", encoding="utf-8")
                theme.write_text('@import "tailwindcss";\n', encoding="utf-8")
                generated_theme.write_text(":root {}\n", encoding="utf-8")

                with self.assertRaisesRegex(axis.CheckError, "root value must be an object"):
                    axis.ui_baseline_payload(root)

    def test_rejects_changed_approved_ui_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            (root / "frontend/src/components/ui/button.tsx").write_text("changed\n", encoding="utf-8")

            issues = axis.ui_baseline_issues(root)

        self.assertIn("approved UI baseline drift", "\n".join(issues))

    def test_rejects_unreviewed_registry_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            (root / "frontend/src/components/ui/input.tsx").write_text("new\n", encoding="utf-8")

            issues = axis.ui_baseline_issues(root)

        self.assertIn("UI baseline has an unreviewed tracked file", "\n".join(issues))

    def test_rejects_unreviewed_registry_support_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            path = root / "frontend/src/hooks/use-mobile.ts"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("export const mobile = false;\n", encoding="utf-8")

            issues = axis.ui_baseline_issues(root)

        self.assertIn("frontend/src/hooks/use-mobile.ts", "\n".join(issues))

    def test_preserves_valid_exception_metadata_when_refreshing_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            baseline_path = root / "frontend/ui-baseline.json"
            baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
            baseline["exceptions"] = {
                "src/components/ui/button.tsx": {
                    "reason": "Compatibility with strict TypeScript settings.",
                    "signOff": "Approved decision reference.",
                }
            }
            baseline_path.write_text(f"{json.dumps(baseline)}\n", encoding="utf-8")

            with contextlib.redirect_stdout(io.StringIO()):
                axis.write_ui_baseline(root)
            refreshed = json.loads(baseline_path.read_text(encoding="utf-8"))

        self.assertEqual(baseline["exceptions"], refreshed["exceptions"])

    def test_refresh_fails_closed_for_invalid_existing_baseline(self) -> None:
        cases = {
            "cannot preserve existing UI baseline": "{\n",
            "root value": "[]\n",
            "`exceptions`": '{"exceptions": []}\n',
        }
        for expected, content in cases.items():
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                self.create_baseline(root)
                (root / "frontend/ui-baseline.json").write_text(content, encoding="utf-8")

                with self.assertRaisesRegex(axis.CheckError, expected):
                    axis.write_ui_baseline(root)

    def test_refresh_fails_closed_when_existing_baseline_cannot_be_read(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            baseline_path = root / "frontend/ui-baseline.json"
            original_read_text = Path.read_text

            def read_text(path: Path, *args: object, **kwargs: object) -> str:
                if path == baseline_path:
                    raise OSError("read failed")
                return original_read_text(path, *args, **kwargs)

            with mock.patch.object(Path, "read_text", autospec=True, side_effect=read_text):
                with self.assertRaisesRegex(axis.CheckError, "read failed"):
                    axis.write_ui_baseline(root)

    def test_rejects_incomplete_exception_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.create_baseline(root)
            baseline_path = root / "frontend/ui-baseline.json"
            baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
            baseline["exceptions"] = {
                "src/components/ui/button.tsx": {"reason": "", "signOff": ""}
            }
            baseline_path.write_text(f"{json.dumps(baseline)}\n", encoding="utf-8")

            issues = axis.ui_baseline_issues(root)

        self.assertIn("requires non-empty `reason` and `signOff`", "\n".join(issues))


class TestFrontendApiContracts(unittest.TestCase):
    def run_frontend_api_contracts(self, files: dict[str, str]) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")

            with (
                mock.patch.object(axis, "ROOT", root),
                contextlib.redirect_stdout(io.StringIO()),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                rc = axis.check_frontend_api_contracts()

            return rc, stderr.getvalue()

    def test_accepts_generated_schema_alias_split_across_lines(self) -> None:
        rc, stderr = self.run_frontend_api_contracts(
            {
                "frontend/src/features/preferences/api.ts": (
                    "import type * as ApiTypes from '@/lib/api-generated';\n"
                    "export type UpdateLanguagePreferenceRequest =\n"
                    "  ApiTypes.UpdateUserLanguagePreferenceRequest;\n"
                )
            }
        )

        self.assertEqual(0, rc, stderr)

    def test_accepts_type_only_request_import_split_across_lines(self) -> None:
        rc, stderr = self.run_frontend_api_contracts(
            {
                "frontend/src/features/rules/guide.tsx": (
                    "import {\n"
                    "  type SearchRuleExpressionGuideRequest,\n"
                    "  searchRuleExpressionGuide,\n"
                    "} from '../api';\n"
                )
            }
        )

        self.assertEqual(0, rc, stderr)

    def test_rejects_hand_authored_frontend_api_model(self) -> None:
        rc, stderr = self.run_frontend_api_contracts(
            {
                "frontend/src/features/preferences/api.ts": (
                    "export type UpdateLanguagePreferenceRequest = {\n"
                    "  language: string;\n"
                    "};\n"
                )
            }
        )

        self.assertEqual(1, rc)
        self.assertIn("Hand-authored frontend API model", stderr)


class TestFrontendTailwindOpacity(unittest.TestCase):
    def issues_for_frontend(self, component_source: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            component = root / "frontend" / "src" / "components" / "Example.tsx"
            component.parent.mkdir(parents=True, exist_ok=True)
            component.write_text(component_source, encoding="utf-8")
            return axis.frontend_tailwind_opacity_issues(root=root)

    def test_rejects_non_scale_opacity_modifier(self) -> None:
        issues = self.issues_for_frontend(
            "export const bad = 'bg-white/28 text-white/52 opacity-58';\n"
        )

        self.assertIn("unsupported Tailwind opacity /28", "\n".join(issues))
        self.assertIn("unsupported Tailwind opacity /52", "\n".join(issues))
        self.assertIn("unsupported Tailwind opacity-58", "\n".join(issues))

    def test_accepts_standard_opacity_scale(self) -> None:
        issues = self.issues_for_frontend("export const ok = 'bg-white/30 text-white/50 opacity-60';\n")

        self.assertEqual([], issues)

    def test_accepts_bracket_opacity_syntax(self) -> None:
        issues = self.issues_for_frontend("export const ok = 'bg-white/[0.28] text-white/[0.52]';\n")

        self.assertEqual([], issues)


class TestFrontendQuality(unittest.TestCase):
    def issues_for_frontend(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_quality_issues(root=root)

    def test_rejects_hand_authored_form_values_interface_in_schema_file(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/schemas/register-schema.ts": (
                    "export interface RegisterFormValues {\n"
                    "  email: string;\n"
                    "}\n"
                    "export function createRegisterSchema() {}\n"
                )
            }
        )

        self.assertIn("must be inferred from the Zod schema", "\n".join(issues))

    def test_rejects_hand_authored_form_values_type_in_schema_file(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/schemas/register-schema.ts": (
                    "export type RegisterFormValues = { email: string };\n"
                )
            }
        )

        self.assertIn("must use z.infer", "\n".join(issues))

    def test_accepts_zod_inferred_form_values_type_in_schema_file(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/schemas/register-schema.ts": (
                    "import { z } from 'zod';\n"
                    "export type RegisterFormValues = z.infer<ReturnType<typeof createRegisterSchema>>;\n"
                    "export function createRegisterSchema() { return z.object({}); }\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_rejects_fire_and_forget_call_in_frontend_tests(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/test/setup.ts": (
                    "beforeEach(() => {\n"
                    "  void i18n.changeLanguage('en');\n"
                    "});\n"
                )
            }
        )

        self.assertIn("must await/return async work", "\n".join(issues))

    def test_accepts_awaited_call_in_frontend_tests(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/test/setup.ts": (
                    "beforeEach(async () => {\n"
                    "  await i18n.changeLanguage('en');\n"
                    "});\n"
                )
            }
        )

        self.assertEqual([], issues)

if __name__ == "__main__":
    unittest.main()
