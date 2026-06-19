from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from unittest import mock
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import axis  # noqa: E402
import doc_drift_domains  # noqa: E402


def load_script(script_name: str):
    path = SCRIPTS / script_name
    return load_python_file(path)


def load_python_file(path: Path):
    module_name = f"_test_{path.name.replace('-', '_').replace('.', '_')}"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"Cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check_pr = load_script("check-pr.py")
check_use_case_docs = load_script("check-use-case-docs.py")
sync_mermaid_theme = load_python_file(ROOT / "docs" / "scripts" / "sync-mermaid-theme.py")


class TestSyncMermaidTheme(unittest.TestCase):
    def test_moves_existing_init_after_banner_to_canonical_first_line(self) -> None:
        old_init = '%%{init: {"theme": "neutral"}}%%'
        content = (
            "```mermaid\n"
            "%% existing banner\n"
            f"{old_init}\n"
            "flowchart TD\n"
            "    A --> B\n"
            "```\n"
        )

        synced, changed = sync_mermaid_theme.sync_mermaid_blocks(content)

        self.assertTrue(changed)
        self.assertEqual(1, synced.count("%%{init:"))
        self.assertIn(
            f"```mermaid\n{sync_mermaid_theme.MERMAID_INIT}\n%% existing banner\nflowchart TD\n",
            synced,
        )


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


class TestUseCaseDocsGate(unittest.TestCase):
    def issues_for_use_case(self, callout: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample" / "README.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# Sample use case

## Purpose

Ship user value.

## Primary actor

- User

## Trigger

- User starts the flow.

## Main flow

1. User starts.
2. System responds.
3. User completes the flow.

## Alternate / error flows

- None.

## Acceptance Criteria

*Happy path*
- [ ] Works.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

"""
                + callout,
                encoding="utf-8",
            )
            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = root
            try:
                return check_use_case_docs.check_file(path)
            finally:
                check_use_case_docs.ROOT = original_root

    def test_rejects_missing_deferred_followups(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | N/A |
>
> **Gaps vs spec:** none.
>
> **Decisions:** N/A.
"""
        )

        self.assertIn("missing implementation status deferred follow-ups section", "\n".join(issues))

    def test_rejects_legacy_deferred_heading(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | N/A |
>
> **Gaps vs spec:** none.
>
> **Deferred:** legacy wording.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("missing implementation status deferred follow-ups section", joined)
        self.assertIn("legacy Deferred status heading found", joined)

    def test_rejects_legacy_deferred_heading_even_when_status_is_not_strict(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample" / "README.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# Sample use case

## Purpose

Ship user value.

## Primary actor

- User

## Trigger

- User starts the flow.

## Main flow

1. User starts.

## Alternate / error flows

- None.

## Acceptance Criteria

- [ ] Works.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

> **Implementation status**
>
> **Deferred:** legacy wording.
""",
                encoding="utf-8",
            )
            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = root
            try:
                issues = check_use_case_docs.check_file(path, strict_status=False)
            finally:
                check_use_case_docs.ROOT = original_root

        self.assertIn("legacy Deferred status heading found", "\n".join(issues))

    def test_rejects_pending_layer_with_empty_gap_and_deferred_status(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | \u23f3 |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** N/A.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("pending layer cannot use `Gaps vs spec: none`", joined)

    def test_rejects_generic_status_placeholder_prose(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | \u23f3 |
>
> **Gaps vs spec:** Open work remains in layers marked pending above.
>
> **Deferred follow-ups:** Complete the open items listed in Gaps vs spec before marking this use case complete.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("implementation status gaps vs spec uses generic placeholder prose", joined)
        self.assertIn("implementation status deferred follow-ups uses generic placeholder prose", joined)

    def test_rejects_empty_status_sections(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | N/A |
>
> **Gaps vs spec:**
>
> **Deferred follow-ups:**
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("implementation status gaps vs spec section is empty", joined)
        self.assertIn("implementation status deferred follow-ups section is empty", joined)

    def test_accepts_required_implementation_status_sections(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | N/A |
> | Application | N/A |
> | Infrastructure | N/A |
> | API | N/A |
> | Frontend | N/A |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertEqual([], issues)

    def test_strips_implementation_status_for_stock_flow_ratchet(self) -> None:
        before = """# Sample

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

> **Implementation status**
>
> **Gaps vs spec:** old.

## Wireframes
"""
        after = before.replace("> **Gaps vs spec:** old.", "> **Gaps vs spec:** new.\n>\n> **Deferred follow-ups:** N/A.")

        self.assertEqual(
            check_use_case_docs.strip_implementation_status_callouts(before),
            check_use_case_docs.strip_implementation_status_callouts(after),
        )

    def test_changed_content_outside_status_uses_merge_base_for_three_dot_range(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample" / "README.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            current = """# Sample

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

> **Implementation status**
>
> **Gaps vs spec:** new.
"""
            previous = current.replace("> **Gaps vs spec:** new.", "> **Gaps vs spec:** old.")
            path.write_text(current, encoding="utf-8")
            calls: list[list[str]] = []

            def fake_run(args: list[str], **_kwargs):
                calls.append(args)
                if args[:2] == ["git", "merge-base"]:
                    return check_use_case_docs.subprocess.CompletedProcess(args, 0, stdout="abc123\n")
                if args[:2] == ["git", "show"]:
                    self.assertEqual(args[2], "abc123:docs/use-cases/example/sample/README.md")
                    return check_use_case_docs.subprocess.CompletedProcess(args, 0, stdout=previous)
                raise AssertionError(f"unexpected subprocess call: {args}")

            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = root
            try:
                with mock.patch.object(check_use_case_docs.subprocess, "run", side_effect=fake_run):
                    changed = check_use_case_docs.changed_use_case_content_outside_status(
                        path,
                        "origin/main...HEAD",
                    )
            finally:
                check_use_case_docs.ROOT = original_root

        self.assertFalse(changed)
        self.assertIn(["git", "merge-base", "origin/main", "HEAD"], calls)

    def test_changed_paths_against_base_include_working_tree_and_untracked(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            calls: list[list[str]] = []
            outputs = {
                ("git", "rev-parse", "--verify", "origin/main"): "",
                ("git", "diff", "--name-only", "origin/main...HEAD"): "docs/use-cases/a/README.md\n",
                ("git", "diff", "--name-only", "--cached"): "docs/use-cases/b/README.md\n",
                ("git", "diff", "--name-only"): "docs/use-cases/c/README.md\n",
                ("git", "ls-files", "--others", "--exclude-standard"): "docs/use-cases/d/README.md\n",
            }

            def fake_run(args: list[str], **_kwargs):
                calls.append(args)
                stdout = outputs.get(tuple(args), "")
                return check_use_case_docs.subprocess.CompletedProcess(args, 0, stdout=stdout)

            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = Path(temp)
            try:
                with mock.patch.object(check_use_case_docs.subprocess, "run", side_effect=fake_run):
                    paths = check_use_case_docs.changed_paths_against_base()
            finally:
                check_use_case_docs.ROOT = original_root

        self.assertEqual(
            [
                root / "docs" / "use-cases" / "a" / "README.md",
                root / "docs" / "use-cases" / "b" / "README.md",
                root / "docs" / "use-cases" / "c" / "README.md",
                root / "docs" / "use-cases" / "d" / "README.md",
            ],
            paths,
        )
        self.assertIn(["git", "diff", "--name-only"], calls)
        self.assertIn(["git", "ls-files", "--others", "--exclude-standard"], calls)


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

    def test_rejects_machine_specific_paths_in_docs(self) -> None:
        issues = self.issue_text([("docs/playbooks/local-dev.md", "cd /mnt/d/projects/axis && docker compose up -d")])
        self.assertIn("Machine-specific local path introduced", issues)

    def test_rejects_windows_user_paths_in_docs(self) -> None:
        issues = self.issue_text([("docs/playbooks/local-dev.md", r"C:\Users\phuon\AppData\Local")])
        self.assertIn("Machine-specific local path introduced", issues)

    def test_accepts_placeholder_paths_in_docs(self) -> None:
        issues = axis.doc_drift_added_line_issues([("docs/playbooks/local-dev.md", "cd <repo-root> && docker compose up -d")])
        self.assertEqual([], issues)


class TestWorkingTreeDiffHelpers(unittest.TestCase):
    def fake_git_run(self, outputs: dict[tuple[str, ...], str]):
        def fake_run(args: list[str], **_kwargs):
            key = tuple(args[1:] if args and args[0] == "git" else args)
            stdout = outputs.get(key, "")
            return axis.subprocess.CompletedProcess(args, 0, stdout=stdout, stderr="")

        return fake_run

    def test_changed_paths_include_committed_staged_unstaged_and_untracked(self) -> None:
        outputs = {
            ("diff", "--name-only", "base...HEAD"): "docs/committed.md\n",
            ("diff", "--name-only", "--cached"): "docs/staged.md\n",
            ("diff", "--name-only"): "docs/unstaged.md\n",
            ("ls-files", "--others", "--exclude-standard"): "docs/untracked.md\n",
        }

        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
        ):
            paths = axis.changed_paths("base...HEAD")

        self.assertEqual(
            ["docs/committed.md", "docs/staged.md", "docs/unstaged.md", "docs/untracked.md"],
            paths,
        )

    def test_changed_name_status_marks_untracked_files_added(self) -> None:
        outputs = {
            ("diff", "--name-status", "base...HEAD"): "M\tdocs/committed.md\n",
            ("diff", "--name-status", "--cached"): "A\tdocs/staged.md\n",
            ("diff", "--name-status"): "M\tdocs/unstaged.md\n",
            ("ls-files", "--others", "--exclude-standard"): "docs/untracked.md\n",
        }

        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
        ):
            changes = axis.changed_name_status("base...HEAD")

        self.assertEqual(
            [
                ["M", "docs/committed.md"],
                ["A", "docs/staged.md"],
                ["M", "docs/unstaged.md"],
                ["A", "docs/untracked.md"],
            ],
            changes,
        )

    def test_added_lines_include_working_tree_and_untracked_content(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            untracked = root / "docs" / "untracked.md"
            untracked.parent.mkdir(parents=True)
            untracked.write_text("untracked line\n", encoding="utf-8")

            outputs = {
                ("diff", "--unified=0", "base...HEAD"): "+++ b/docs/committed.md\n++++heading\n+committed line\n",
                ("diff", "--unified=0", "--cached"): "+++ b/docs/staged.md\n+staged line\n",
                ("diff", "--unified=0"): "+++ b/docs/unstaged.md\n+unstaged line\n",
                ("ls-files", "--others", "--exclude-standard"): "docs/untracked.md\n",
            }

            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "exe", side_effect=lambda name: name),
                mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
            ):
                rows = list(axis.added_lines("base...HEAD", lambda _path: True))

        self.assertEqual(
            [
                ("docs/committed.md", "+++heading"),
                ("docs/committed.md", "committed line"),
                ("docs/staged.md", "staged line"),
                ("docs/unstaged.md", "unstaged line"),
                ("docs/untracked.md", "untracked line"),
            ],
            rows,
        )


class TestVulnerablePackageGate(unittest.TestCase):
    def test_uses_absolute_solution_path_for_dotnet_list(self) -> None:
        calls: list[list[str]] = []

        def fake_run(args: list[str], **_kwargs):
            calls.append(args)
            return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.check_vulnerable_packages())

        self.assertEqual("dotnet", calls[0][0])
        self.assertEqual(str(axis.ROOT / "Axis.sln"), calls[0][2])
        self.assertTrue(Path(calls[0][2]).is_absolute())


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
        self.assertIn("standard UI control <button> must use a shared shadcn/ui primitive", joined)
        self.assertIn("standard UI control <input> must use a shared shadcn/ui primitive", joined)

    def test_accepts_native_standard_controls_inside_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\"><input /></button>;\n"
                    "}\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_rejects_text_button_without_icon(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/ForgotPasswordPage.tsx": (
                    "import { Button } from '@/components/ui/button';\n"
                    "export function ForgotPasswordPage() {\n"
                    "  return <Button variant=\"cta\">Send reset link</Button>;\n"
                    "}\n"
                )
            }
        )

        self.assertIn("text Button must include an icon child", "\n".join(issues))

    def test_accepts_text_button_with_icon(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/auth/components/ForgotPasswordPage.tsx": (
                    "import { Send } from 'lucide-react';\n"
                    "import { Button } from '@/components/ui/button';\n"
                    "export function ForgotPasswordPage() {\n"
                    "  return <Button variant=\"cta\"><Send aria-hidden />Send reset link</Button>;\n"
                    "}\n"
                )
            }
        )

        self.assertEqual([], issues)

    def test_accepts_segmented_text_toggle_without_icon(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/features/preferences/components/PreferenceControls.tsx": (
                    "import { Button } from '@/components/ui/button';\n"
                    "export function PreferenceControls() {\n"
                    "  return <Button type=\"button\" aria-pressed={true} size=\"xs\">EN</Button>;\n"
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
            "AGENTS.md",
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
            "AGENTS.md",
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

    def test_rejects_missing_pre_push_quick_gate_delegate(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            hook = Path("scripts/hooks/pre-push")
            files[hook] = files[hook].replace(
                'root / "scripts" / "axis.py"), "pre-push"',
                'root / "scripts" / "other.py"), "pre-push"',
            )

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


class TestDocSizeBudgetGate(unittest.TestCase):
    def issues_for_files(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.doc_size_budget_issues(root=root)

    def test_rejects_overlong_pattern_router(self) -> None:
        issues = self.issues_for_files(
            {"docs/playbooks/patterns.md": "\n".join("line" for _ in range(151))}
        )

        self.assertIn("150-line docs budget", "\n".join(issues))

    def test_rejects_overlong_playbook(self) -> None:
        issues = self.issues_for_files(
            {"docs/playbooks/api-patterns.md": "\n".join("line" for _ in range(301))}
        )

        self.assertIn("300-line docs budget", "\n".join(issues))

    def test_current_repository_doc_size_budgets_still_pass(self) -> None:
        self.assertEqual([], axis.doc_size_budget_issues())


class TestScriptsStandardGate(unittest.TestCase):
    def issues_for_files(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.non_python_utility_script_issues(root=root)

    def test_rejects_non_python_docs_utility_script(self) -> None:
        issues = self.issues_for_files({"docs/scripts/render-wireframes.mjs": "console.log('nope');\n"})
        self.assertIn(
            "docs/scripts/render-wireframes.mjs: docs-level utility scripts must be Python; "
            "native tooling belongs beside its owning package",
            issues,
        )

    def test_rejects_non_python_docs_utility_script_case_insensitive(self) -> None:
        issues = self.issues_for_files({"docs/scripts/render-wireframes.MJS": "console.log('nope');\n"})
        self.assertIn(
            "docs/scripts/render-wireframes.MJS: docs-level utility scripts must be Python; "
            "native tooling belongs beside its owning package",
            issues,
        )

    def test_accepts_python_docs_utility_native_frontend_tooling_and_wireframe_assets(self) -> None:
        issues = self.issues_for_files(
            {
                "docs/scripts/sync-mermaid-theme.py": "print('ok')\n",
                "docs/wireframes/app-shell.excalidraw": "{}\n",
                "docs/wireframes/app-shell.svg": "<svg />\n",
                "docs/diagrams/mermaid_theme.py": "MERMAID_INIT = ''\n",
                "frontend/package.json": '{"scripts":{"export:wireframes":"node scripts/export-wireframes.mjs"}}\n',
                "frontend/scripts/export-wireframes.mjs": "console.log('native package tooling');\n",
            }
        )
        self.assertEqual([], issues)

    def test_rejects_non_python_pre_push_hook(self) -> None:
        issues = self.issues_for_files(
            {"scripts/hooks/pre-push": "#!/usr/bin/env bash\npython scripts/axis.py pre-push\n"}
        )
        self.assertIn("scripts/hooks/pre-push: pre-push hook must be a Python entrypoint", issues)

    def test_current_repository_scripts_standard_still_passes(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_scripts_standard())


class TestCodexSkillsGate(unittest.TestCase):
    def issues_for_skill(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.codex_skill_issues(root=root)

    def valid_skill_files(self) -> dict[str, str]:
        return {
            ".agents/skills/axis-example/SKILL.md": (
                "---\n"
                "name: axis-example\n"
                "description: Use when Codex needs to perform a concrete Axis example workflow with repo-specific checks.\n"
                "---\n"
                "\n"
                "# Axis Example\n"
                "\n"
                "Run the example workflow.\n"
            ),
            ".agents/skills/axis-example/agents/openai.yaml": (
                "interface:\n"
                "  display_name: \"Axis Example\"\n"
                "  short_description: \"Check example skill metadata\"\n"
                "  default_prompt: \"Use $axis-example to run the example workflow.\"\n"
            ),
        }

    def test_accepts_valid_repo_skill(self) -> None:
        self.assertEqual([], self.issues_for_skill(self.valid_skill_files()))

    def test_rejects_template_todo_text(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nTODO: finish this later.\n"

        issues = self.issues_for_skill(files)

        self.assertIn("remove template TODO text", "\n".join(issues))

    def test_rejects_frontmatter_name_mismatch(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("name: axis-example", "name: axis-other")

        issues = self.issues_for_skill(files)

        self.assertIn("frontmatter name must match folder name", "\n".join(issues))

    def test_rejects_default_prompt_without_skill_invocation(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/agents/openai.yaml"] = files[
            ".agents/skills/axis-example/agents/openai.yaml"
        ].replace("$axis-example", "$other-skill")

        issues = self.issues_for_skill(files)

        self.assertIn("default_prompt must mention $axis-example", "\n".join(issues))

    def test_rejects_missing_skill_doc_reference(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nRead `docs/playbooks/missing.md`.\n"

        issues = self.issues_for_skill(files)

        self.assertIn("referenced path `docs/playbooks/missing.md` does not exist", "\n".join(issues))

    def test_accepts_existing_skill_doc_reference(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nRead `docs/playbooks/frontend.md`.\n"
        files["docs/playbooks/frontend.md"] = "# Frontend\n"

        self.assertEqual([], self.issues_for_skill(files))

    def test_rejects_missing_markdown_anchor_reference(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nRead [Frontend](docs/playbooks/frontend.md#missing).\n"
        files["docs/playbooks/frontend.md"] = "# Frontend\n"

        issues = self.issues_for_skill(files)

        self.assertIn("referenced anchor `docs/playbooks/frontend.md#missing` does not exist", "\n".join(issues))

    def test_rejects_overlong_skill_body(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\n" + "\n".join("Extra line." for _ in range(130))

        issues = self.issues_for_skill(files)

        self.assertIn("keep SKILL.md concise", "\n".join(issues))

    def test_rejects_ambiguous_best_effort_wording(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nMaybe run the check if you have time.\n"

        issues = self.issues_for_skill(files)

        self.assertIn("replace ambiguous best-effort wording", "\n".join(issues))

    def test_rejects_api_contract_skill_without_required_chaining(self) -> None:
        files = {
            ".agents/skills/axis-api-contract/SKILL.md": (
                "---\n"
                "name: axis-api-contract\n"
                "description: Use when Codex changes Axis REST API contracts with generated frontend types.\n"
                "---\n"
                "\n"
                "# Axis API Contract\n"
                "\n"
                "Change the API contract.\n"
            ),
            ".agents/skills/axis-api-contract/agents/openai.yaml": (
                "interface:\n"
                "  display_name: \"Axis API Contract\"\n"
                "  short_description: \"Change Axis API contracts safely\"\n"
                "  default_prompt: \"Use $axis-api-contract to change this API contract.\"\n"
            ),
        }

        issues = self.issues_for_skill(files)

        joined = "\n".join(issues)
        self.assertIn("must chain to $axis-design-gate", joined)
        self.assertIn("must chain to $axis-ready-review", joined)

    def test_current_repository_codex_skills_still_pass(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_codex_skills())


class TestDoctorPythonPackageChecks(unittest.TestCase):
    def test_python_module_version_rejects_missing_package(self) -> None:
        with mock.patch.object(axis.importlib.util, "find_spec", return_value=None):
            status, detail = axis._python_module_version("yaml", "PyYAML")

        self.assertEqual("FAIL", status)
        self.assertIn("PyYAML is not installed", detail)

    def test_python_module_version_reports_package_version(self) -> None:
        class Module:
            __version__ = "6.0.3"

        with (
            mock.patch.object(axis.importlib.util, "find_spec", return_value=object()),
            mock.patch.object(axis.importlib, "import_module", return_value=Module),
        ):
            status, detail = axis._python_module_version("yaml", "PyYAML")

        self.assertEqual("OK", status)
        self.assertIn("PyYAML 6.0.3", detail)


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
