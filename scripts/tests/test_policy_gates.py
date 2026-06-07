from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis  # noqa: E402
import doc_drift_domains  # noqa: E402


def load_script(script_name: str):
    path = SCRIPTS / script_name
    module_name = f"_test_{script_name.replace('-', '_').replace('.', '_')}"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"Cannot load {script_name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check_pr = load_script("check-pr.py")


class TestTestNamingGate(unittest.TestCase):
    def run_test_naming(self, source: str) -> int:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            test_dir = root / "tests" / "Example"
            test_dir.mkdir(parents=True)
            (test_dir / "ExampleTests.cs").write_text(source, encoding="utf-8")

            original_root = axis.ROOT
            axis.ROOT = root
            try:
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    return axis.check_test_naming()
            finally:
                axis.ROOT = original_root

    def test_rejects_non_three_segment_test_names(self) -> None:
        rc = self.run_test_naming(
            """
public sealed class ExampleTests
{
    [Fact]
    public void BadName() {}
}
"""
        )
        self.assertNotEqual(0, rc)

    def test_accepts_subject_condition_outcome_names(self) -> None:
        rc = self.run_test_naming(
            """
public sealed class ExampleTests
{
    [Fact]
    public void Widget_WhenInputIsValid_ReturnsSuccess() {}
}
"""
        )
        self.assertEqual(0, rc)

    def test_current_repository_test_names_still_pass(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_test_naming())


class TestPrGuard(unittest.TestCase):
    def test_rejects_unchecked_requirement_without_na_reason(self) -> None:
        body = """## Summary
This summary is long enough.

## Linked spec
docs/use-cases/example/README.md

## Requirements & rules followed
- [ ] **Verification gate** - local checks
"""
        self.assertTrue(check_pr.validate("feat(example): improve gates", body))

    def test_accepts_checked_requirement(self) -> None:
        body = """## Summary
This summary is long enough.

## Linked spec
docs/use-cases/example/README.md

## Requirements & rules followed
- [x] **Verification gate** - local checks
"""
        self.assertEqual([], check_pr.validate("feat(example): improve gates", body))


class TestDocDriftRatchets(unittest.TestCase):
    def issue_text(self, rows: list[tuple[str, str]]) -> str:
        return "\n".join(axis.doc_drift_added_line_issues(rows))

    def test_rejects_skipped_tests(self) -> None:
        issues = self.issue_text([("tests/ExampleTests.cs", '[Fact(Skip = "later")]')])
        self.assertIn("Skipped test introduced", issues)

    def test_rejects_ensure_created(self) -> None:
        issues = self.issue_text([("tests/Fixture.cs", "await db.Database.EnsureCreatedAsync();")])
        self.assertIn("EnsureCreated introduced", issues)

    def test_rejects_datetime_now_in_src_or_tests(self) -> None:
        issues = self.issue_text([("src/Example.cs", "var now = DateTime.Now;")])
        self.assertIn("DateTime.Now introduced", issues)

    def test_ignores_todo_in_docs(self) -> None:
        issues = axis.doc_drift_added_line_issues([("docs/example.md", "TODO in docs is not this gate")])
        self.assertEqual([], issues)


class TestFrontendRadiusTokens(unittest.TestCase):
    def issues_for_frontend(self, component_source: str, css_radius: str = "0.5rem") -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            css = root / "frontend" / "src" / "index.css"
            component = root / "frontend" / "src" / "components" / "Example.tsx"
            css.parent.mkdir(parents=True, exist_ok=True)
            component.parent.mkdir(parents=True, exist_ok=True)
            css.write_text(f":root {{\n  --radius: {css_radius};\n}}\n", encoding="utf-8")
            component.write_text(component_source, encoding="utf-8")
            return axis.frontend_radius_token_issues(root=root)

    def test_rejects_radius_token_drift(self) -> None:
        issues = self.issues_for_frontend("export const ok = 'rounded-md';\n", css_radius="0.375rem")

        self.assertIn("--radius must stay 0.5rem", "\n".join(issues))

    def test_rejects_oversized_radius_utilities(self) -> None:
        issues = self.issues_for_frontend("export const bad = 'rounded-2xl';\n")

        self.assertIn("avoid radius above 8px", "\n".join(issues))

    def test_rejects_arbitrary_non_token_radius(self) -> None:
        issues = self.issues_for_frontend("export const bad = 'rounded-[18px]';\n")

        self.assertIn("use shared radius tokens", "\n".join(issues))

    def test_accepts_standard_radius_tokens(self) -> None:
        issues = self.issues_for_frontend("export const ok = 'rounded-sm rounded-md rounded-lg';\n")

        self.assertEqual([], issues)


class TestFrontendComponentComposition(unittest.TestCase):
    def issues_for_frontend(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_component_composition_issues(root=root)

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

    def test_rejects_duplicated_flow_trace_geometry(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/AuthCard.tsx": (
                    "export function BadTrace() {\n"
                    "  return <div className=\"grid grid-cols-[34px_1fr] gap-4\" />;\n"
                    "}\n"
                )
            }
        )

        self.assertIn("duplicated flow/timeline geometry", "\n".join(issues))

    def test_rejects_duplicated_access_path_trace(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/AuthSignalPanel.tsx": (
                    "const steps = [{ labelKey: 'landing.signInStep' }];\n"
                )
            }
        )

        self.assertIn("duplicated access path trace", "\n".join(issues))

    def test_rejects_hard_coded_public_auth_navigation_cta(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "import { Link } from '@tanstack/react-router';\n"
                    "export function LandingActions() {\n"
                    "  return <Link to=\"/login\" className=\"inline-flex h-9 items-center\">Sign in</Link>;\n"
                    "}\n"
                )
            }
        )

        self.assertIn("navigation CTA styling must use ActionLink", "\n".join(issues))

    def test_accepts_shared_public_auth_navigation_cta(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "import { ActionLink } from '@/components/ui/action-link';\n"
                    "import { LogIn } from 'lucide-react';\n"
                    "export function LandingActions() {\n"
                    "  return <ActionLink to=\"/login\" icon={LogIn}>Sign in</ActionLink>;\n"
                    "}\n"
                )
            }
        )

        self.assertEqual([], issues)


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
                "frontend/src/features/auth/schemas/login-schema.ts": (
                    "export interface LoginFormValues {\n"
                    "  email: string;\n"
                    "}\n"
                    "export function createLoginSchema() {}\n"
                )
            }
        )

        self.assertIn("must be inferred from the Zod schema", "\n".join(issues))

    def test_rejects_hand_authored_form_values_type_in_schema_file(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/schemas/login-schema.ts": (
                    "export type LoginFormValues = { email: string };\n"
                )
            }
        )

        self.assertIn("must use z.infer", "\n".join(issues))

    def test_accepts_zod_inferred_form_values_type_in_schema_file(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/schemas/login-schema.ts": (
                    "import { z } from 'zod';\n"
                    "export type LoginFormValues = z.infer<ReturnType<typeof createLoginSchema>>;\n"
                    "export function createLoginSchema() { return z.object({}); }\n"
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


class TestGovernanceOwnerBoundary(unittest.TestCase):
    def issues_for_doc(self, relative_path: str, content: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
            return axis.governance_owner_boundary_issues(root=root)

    def test_rejects_policy_command_restatement_in_entry_docs(self) -> None:
        issues = self.issues_for_doc(
            "CLAUDE.md",
            "Run `python scripts/axis.py check policy-tests` before push.\n",
        )

        self.assertEqual(1, len(issues))
        self.assertIn("governance doc restates", issues[0])

    def test_rejects_design_gate_as_machine_gate_wording(self) -> None:
        issues = self.issues_for_doc(
            "CONTRIBUTING.md",
            "Design Gate is a CI gate for high-risk work.\n",
        )

        self.assertEqual(1, len(issues))
        self.assertIn("review artifact", issues[0])

    def test_allows_design_gate_review_artifact_wording(self) -> None:
        issues = self.issues_for_doc(
            "CLAUDE.md",
            "Design Gate is a required review artifact, not a machine-enforced CI gate.\n",
        )

        self.assertEqual([], issues)

    def test_current_repository_governance_owner_boundaries_still_pass(self) -> None:
        self.assertEqual([], axis.governance_owner_boundary_issues())


class TestReviewFindingsRegistry(unittest.TestCase):
    def issues_for_review_findings(self, ledger_rows: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "REVIEW_FINDINGS.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# Review findings

## Ledger

| Finding class | Rule owner | Trigger / scope | Mechanism | Proof / gap | Status |
|---|---|---|---|---|---|
"""
                + ledger_rows,
                encoding="utf-8",
            )
            return axis.review_findings_registry_issues(root=root)

    def test_rejects_missing_rule_owner(self) -> None:
        issues = self.issues_for_review_findings(
            "| Example finding |  | PR scope | CI job | negative test | **Enforced** |\n"
        )

        self.assertIn("Rule owner", "\n".join(issues))

    def test_rejects_unknown_status(self) -> None:
        issues = self.issues_for_review_findings(
            "| Example finding | This file | PR scope | Review | Human review | **Mandatory** |\n"
        )

        self.assertIn("unknown ledger status", "\n".join(issues))

    def test_rejects_partial_without_known_gap(self) -> None:
        issues = self.issues_for_review_findings(
            "| Example finding | This file | PR scope | Diff ratchet | Untouched files are not swept | **Partial** |\n"
        )

        self.assertIn("Partial row must name a known gap", "\n".join(issues))

    def test_rejects_review_only_gate_language(self) -> None:
        issues = self.issues_for_review_findings(
            "| Example finding | This file | PR scope | CI gate | Human review | **Review-only** |\n"
        )

        self.assertIn("must not use gate/enforced language", "\n".join(issues))

    def test_current_repository_review_findings_registry_still_passes(self) -> None:
        self.assertEqual([], axis.review_findings_registry_issues())


class TestEnforcementTruthAudit(unittest.TestCase):
    def write_truth_repo(self, root: Path, mutate=None) -> None:
        files: dict[Path, str] = {}
        for relative, requirements in axis.ENFORCEMENT_TRUTH_REQUIRED_SNIPPETS:
            files[relative] = "\n".join(snippet for snippet, _description in requirements) + "\n"

        workflow = Path(".github/workflows/build-and-test.yml")
        files[workflow] += "- 'openapi.json'\n- 'openapi.json'\n"

        if mutate is not None:
            mutate(files)

        for relative, content in files.items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")

    def test_rejects_ci_without_doc_drift(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            workflow = Path(".github/workflows/build-and-test.yml")
            files[workflow] = files[workflow].replace("run: python scripts/axis.py check doc-drift\n", "")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("doc drift runs in CI", "\n".join(issues))

    def test_rejects_missing_pre_push_verify_delegate(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            hook = Path("scripts/hooks/pre-push")
            files[hook] = files[hook].replace('scripts/axis.py" verify', 'scripts/other.py" verify')

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("pre-push delegates", "\n".join(issues))

    def test_rejects_missing_analyzer_warnings_as_errors(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            build_props = Path("Directory.Build.props")
            files[build_props] = files[build_props].replace(
                "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>",
                "<TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("build treats warnings as errors", "\n".join(issues))

    def test_rejects_openapi_not_triggering_both_ci_filters(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            workflow = Path(".github/workflows/build-and-test.yml")
            files[workflow] = files[workflow].replace("- 'openapi.json'\n- 'openapi.json'\n", "- 'openapi.json'\n")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("both backend and frontend CI filters", "\n".join(issues))

    def test_current_repository_enforcement_truth_audit_still_passes(self) -> None:
        self.assertEqual([], axis.enforcement_truth_audit_issues())


class TestTextEncodingGate(unittest.TestCase):
    def issues_for_file(self, name: str, content: bytes) -> str:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
            return "\n".join(axis.text_encoding_issues([path], root=root))

    def test_rejects_utf8_bom(self) -> None:
        issues = self.issues_for_file("docs/example.md", b"\xef\xbb\xbf# Title\n")
        self.assertIn("UTF-8 BOM found", issues)

    def test_rejects_invalid_utf8(self) -> None:
        issues = self.issues_for_file("docs/example.md", b"# Title\n\xff\n")
        self.assertIn("invalid UTF-8 byte", issues)

    def test_rejects_crlf_line_endings(self) -> None:
        issues = self.issues_for_file("docs/example.md", b"# Title\r\n")
        self.assertIn("CRLF/CR line ending", issues)

    def test_rejects_common_mojibake_markers(self) -> None:
        mojibake_dash = "—".encode("utf-8").decode("cp1252")
        issues = self.issues_for_file("docs/example.md", f"Broken {mojibake_dash} dash\n".encode("utf-8"))
        self.assertIn("mojibake marker found", issues)

    def test_accepts_utf8_unicode_without_bom_and_lf(self) -> None:
        issues = self.issues_for_file("docs/example.md", "Tiếng Việt → ✅\n".encode("utf-8"))
        self.assertEqual("", issues)

    def test_accepts_valid_latin_capital_a_with_circumflex(self) -> None:
        issues = self.issues_for_file("docs/example.md", "Ângström\n".encode("utf-8"))
        self.assertEqual("", issues)

    def test_current_repository_text_encoding_still_passes(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_text_encoding())


class TestHandlerTestRatchet(unittest.TestCase):
    def test_modified_handler_requires_matching_test_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            changes = [["M", "src/Modules/Billing/Axis.Billing.Application/Commands/CreateInvoiceHandler.cs"]]
            issues = axis.missing_handler_test_issues(changes, root=root)
        self.assertEqual(
            [
                "Handler src/Modules/Billing/Axis.Billing.Application/Commands/CreateInvoiceHandler.cs - "
                "create tests/Modules/Billing/Axis.Billing.Application.Tests/Commands/CreateInvoiceHandlerTests.cs"
            ],
            issues,
        )

    def test_modified_handler_passes_when_matching_test_file_exists(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            test_file = (
                root
                / "tests"
                / "Modules"
                / "Billing"
                / "Axis.Billing.Application.Tests"
                / "Commands"
                / "CreateInvoiceHandlerTests.cs"
            )
            test_file.parent.mkdir(parents=True)
            test_file.write_text("", encoding="utf-8")
            changes = [["M", "src/Modules/Billing/Axis.Billing.Application/Commands/CreateInvoiceHandler.cs"]]
            issues = axis.missing_handler_test_issues(changes, root=root)
        self.assertEqual([], issues)

    def test_deleted_handler_does_not_require_test_file(self) -> None:
        changes = [["D", "src/Modules/Billing/Axis.Billing.Application/Commands/CreateInvoiceHandler.cs"]]
        self.assertEqual([], axis.missing_handler_test_issues(changes))


class TestDocDomainDiscovery(unittest.TestCase):
    def test_module_code_change_alone_does_not_force_doc_activity(self) -> None:
        self.assertEqual(
            [],
            doc_drift_domains.check_readme_api_status(["src/Modules/Identity/Axis.Identity.Domain/User.cs"]),
        )


if __name__ == "__main__":
    unittest.main()
