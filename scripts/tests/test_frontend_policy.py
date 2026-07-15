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


class TestUiBaseline(unittest.TestCase):
    def create_baseline(self, root: Path) -> None:
        files = {
            "frontend/components.json": '{"style":"base-nova"}\n',
            "frontend/src/index.css": '@import "tailwindcss";\n',
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
                config.parent.mkdir(parents=True)
                theme.parent.mkdir(parents=True)
                config.write_text(f"{value}\n", encoding="utf-8")
                theme.write_text('@import "tailwindcss";\n', encoding="utf-8")

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
                    "import type { components } from '@/lib/api-types';\n"
                    "export type UpdateLanguagePreferenceRequest =\n"
                    "  components['schemas']['UpdateUserLanguagePreferenceRequest'];\n"
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

    def test_rejects_public_route_without_route_navigation_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/invite.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "export const Route = createLazyFileRoute('/invite')({ component: InvitePage });\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("public route must export `routeNavigation = publicRouteNavigation(...)`", joined)
        self.assertIn("must use '@/lib/route-navigation'", joined)

    def test_accepts_public_route_with_route_navigation_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/invite.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/'] });\n"
                    "export const Route = createLazyFileRoute('/invite')({ component: InvitePage });\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_skips_redirect_only_and_authenticated_routes_for_route_navigation_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { createLazyFileRoute, Navigate } from '@tanstack/react-router';\n"
                    "export const Route = createLazyFileRoute('/')({ component: () => <Navigate to='/sign-in' /> });\n"
                ),
                "frontend/src/routes/index.tsx": (
                    "import { createFileRoute } from '@tanstack/react-router';\n"
                    "import { redirectFromAppEntryRoute } from '@/features/auth/route-guards';\n"
                    "export const Route = createFileRoute('/')({ beforeLoad: redirectFromAppEntryRoute });\n"
                ),
                "frontend/src/routes/_authenticated/dashboard.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "export const Route = createLazyFileRoute('/_authenticated/dashboard')({ component: DashboardPage });\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_rejects_guest_only_auth_route_outside_guest_group(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/sign-in.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/register'] });\n"
                    "export const Route = createLazyFileRoute('/sign-in')({ component: SignInPage });\n"
                )
            }
        )

        self.assertIn("guest-only auth routes must live under the `_guest`", "\n".join(issues))

    def test_accepts_guest_only_auth_route_under_guest_group(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/_guest.tsx": (
                    "import { createFileRoute, Outlet } from '@tanstack/react-router';\n"
                    "import { redirectAuthenticatedUserFromGuestRoute } from '@/features/auth/route-guards';\n"
                    "export const Route = createFileRoute('/_guest')({ beforeLoad: redirectAuthenticatedUserFromGuestRoute, component: Outlet });\n"
                ),
                "frontend/src/routes/_guest/sign-in.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/register'] });\n"
                    "export const Route = createLazyFileRoute('/_guest/sign-in')({ component: SignInPage });\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_rejects_guest_group_without_guard(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/_guest.tsx": (
                    "import { createFileRoute, Outlet } from '@tanstack/react-router';\n"
                    "export const Route = createFileRoute('/_guest')({ component: Outlet });\n"
                ),
                "frontend/src/routes/_guest/sign-in.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/register'] });\n"
                    "export const Route = createLazyFileRoute('/_guest/sign-in')({ component: SignInPage });\n"
                ),
            }
        )

        self.assertIn("`_guest` route group must own the guest-only redirect guard", "\n".join(issues))

    def test_rejects_guest_leaf_route_with_own_guard(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/_guest.tsx": (
                    "import { createFileRoute, Outlet } from '@tanstack/react-router';\n"
                    "import { redirectAuthenticatedUserFromGuestRoute } from '@/features/auth/route-guards';\n"
                    "export const Route = createFileRoute('/_guest')({ beforeLoad: redirectAuthenticatedUserFromGuestRoute, component: Outlet });\n"
                ),
                "frontend/src/routes/_guest/sign-in.tsx": (
                    "import { createFileRoute } from '@tanstack/react-router';\n"
                    "import { redirectAuthenticatedUserFromGuestRoute } from '@/features/auth/route-guards';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/register'] });\n"
                    "export const Route = createFileRoute('/_guest/sign-in')({ beforeLoad: redirectAuthenticatedUserFromGuestRoute });\n"
                ),
            }
        )

        self.assertIn("guest leaf routes inherit the `_guest` guard", "\n".join(issues))

    def test_rejects_lazy_callback_route_because_success_handoff_must_preload(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/callback.lazy.tsx": (
                    "import { createLazyFileRoute } from '@tanstack/react-router';\n"
                    "import { CallbackPage } from '@/features/auth/components/CallbackPage';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/sign-in'] });\n"
                    "export const Route = createLazyFileRoute('/callback')({ component: CallbackPage });\n"
                )
            }
        )

        self.assertIn("callback success handoffs must run before render", "\n".join(issues))

    def test_rejects_callback_route_without_before_load_handoff(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/callback.tsx": (
                    "import { createFileRoute } from '@tanstack/react-router';\n"
                    "import { CallbackPage } from '@/features/auth/components/CallbackPage';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/sign-in'] });\n"
                    "export const Route = createFileRoute('/callback')({ component: CallbackPage });\n"
                )
            }
        )

        self.assertIn("must perform successful token handoff in `beforeLoad`", "\n".join(issues))

    def test_rejects_token_exchange_or_pending_copy_inside_callback_page(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/CallbackPage.tsx": (
                    "import { exchangeAuthorizationCode } from '@/features/auth/api';\n"
                    "export function CallbackPage() {\n"
                    "  void exchangeAuthorizationCode('code');\n"
                    "  return <p>{t('auth.callback.completing')}</p>;\n"
                    "}\n"
                )
            }
        )

        joined = "\n".join(issues)
        self.assertIn("exchange tokens in the route handoff guard", joined)
        self.assertIn("remove transient callback success copy", joined)

    def test_rejects_stale_callback_pending_translations(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/preferences/translations.ts": (
                    "export const translations = {\n"
                    "  en: { 'auth.callback.title': 'Completing sign-in' },\n"
                    "};\n"
                )
            }
        )

        self.assertIn("stale callback pending translation", "\n".join(issues))

    def test_accepts_callback_route_with_preload_handoff_and_recovery_component(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/callback.tsx": (
                    "import { createFileRoute } from '@tanstack/react-router';\n"
                    "import { CallbackPage } from '@/features/auth/components/CallbackPage';\n"
                    "import { redirectFromCallbackRoute } from '@/features/auth/route-guards';\n"
                    "import { publicRouteNavigation } from '@/lib/route-navigation';\n"
                    "export const routeNavigation = publicRouteNavigation({ escapeTargets: ['/sign-in'] });\n"
                    "export const Route = createFileRoute('/callback')({ beforeLoad: redirectFromCallbackRoute, component: CallbackPage });\n"
                ),
                "frontend/src/features/auth/components/CallbackPage.tsx": (
                    "export function CallbackPage() { return <p>Try signing in again.</p>; }\n"
                ),
                "frontend/src/features/preferences/translations.ts": (
                    "export const translations = { en: { 'auth.callback.invalid': 'Invalid' } };\n"
                ),
            }
        )

        self.assertEqual([], issues)



if __name__ == "__main__":
    unittest.main()
