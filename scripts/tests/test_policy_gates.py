from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import subprocess
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
import axis_repo  # noqa: E402
import doc_drift_domains  # noqa: E402


class EncodingCheckingStream:
    def __init__(self) -> None:
        self.encoding = "cp1252"
        self.errors = "strict"
        self.writes: list[str] = []
        self.reconfigure_calls: list[dict[str, str]] = []

    def reconfigure(self, **kwargs) -> None:
        self.reconfigure_calls.append(kwargs)
        self.encoding = kwargs.get("encoding", self.encoding)
        self.errors = kwargs.get("errors", self.errors)

    def write(self, text: str) -> int:
        text.encode(self.encoding, self.errors)
        self.writes.append(text)
        return len(text)

    def flush(self) -> None:
        pass

    def getvalue(self) -> str:
        return "".join(self.writes)


def load_script(script_name: str):
    path = SCRIPTS / script_name
    return load_python_file(path)


def load_python_file(path: Path):
    module_name = f"_test_{path.name.replace('-', '_').replace('.', '_')}"
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"Cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


check_pr = load_script("check-pr.py")
check_local_dev_docs = load_script("check-local-dev-docs.py")
check_use_case_docs = load_script("check-use-case-docs.py")
check_foundation_docs = load_script("check-foundation-docs.py")


class TestCliTextStreams(unittest.TestCase):
    def test_configures_stdout_and_stderr_as_utf8(self) -> None:
        stdout = EncodingCheckingStream()
        stderr = EncodingCheckingStream()

        with mock.patch.object(axis.sys, "stdout", stdout), mock.patch.object(axis.sys, "stderr", stderr):
            axis.configure_cli_text_streams()

        self.assertEqual([{"encoding": "utf-8", "errors": "replace"}], stdout.reconfigure_calls)
        self.assertEqual([{"encoding": "utf-8", "errors": "replace"}], stderr.reconfigure_calls)

    def test_main_configures_streams_before_emitting_tool_unicode(self) -> None:
        stdout = EncodingCheckingStream()
        stderr = EncodingCheckingStream()
        lychee_output = "🔍 1 Total (in 0s) ✅ 1 OK 🚫 0 Errors\n"

        with (
            mock.patch.object(axis.sys, "stdout", stdout),
            mock.patch.object(axis.sys, "stderr", stderr),
            mock.patch.object(axis, "find_lychee", return_value="/usr/bin/lychee"),
            mock.patch.object(
                axis,
                "run_optional",
                return_value=axis.subprocess.CompletedProcess(
                    ["/usr/bin/lychee", "--version"],
                    0,
                    stdout="lychee 0.23.0\n",
                    stderr="",
                ),
            ),
            mock.patch.object(
                axis,
                "run_lychee_markdown_check",
                return_value=axis.subprocess.CompletedProcess(
                    ["/usr/bin/lychee"],
                    0,
                    stdout=lychee_output,
                    stderr="",
                ),
            ),
        ):
            self.assertEqual(0, axis.main(["check", "markdown-links"]))

        self.assertIn(lychee_output, stdout.getvalue())


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
    def test_accepts_project_branch_convention(self) -> None:
        for branch in (
            "feat/add-workspace",
            "fix/restore-tabs",
            "docs/clarify-workflow",
            "refactor/standardize-ui-governance",
            "test/cover-branch-policy",
            "chore/update-tooling",
            "renovate/all-non-major",
        ):
            with self.subTest(branch=branch):
                self.assertEqual([], check_pr.validate_branch(branch))

    def test_rejects_non_project_branch_convention(self) -> None:
        for branch in ("", "main", "agent/add-workspace", "feat/AddWorkspace", "feat/nested/name"):
            with self.subTest(branch=branch):
                self.assertTrue(check_pr.validate_branch(branch))

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
    def issues_for_document(
        self,
        content: str,
        *,
        evidence_doc: str | None = None,
        evidence_files: tuple[str, ...] = (),
    ) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            for evidence_file in evidence_files:
                evidence_path = root / evidence_file
                evidence_path.parent.mkdir(parents=True, exist_ok=True)
                evidence_path.write_text("proof\n", encoding="utf-8")
            path.write_text(content, encoding="utf-8")
            if evidence_doc is not None:
                path.with_name("sample.evidence.md").write_text(evidence_doc, encoding="utf-8")
            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = root
            try:
                return check_use_case_docs.check_file(path)
            finally:
                check_use_case_docs.ROOT = original_root

    def test_use_case_inventory_layout_accepts_direct_markdown_files(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            domain = use_cases / "example"
            domain.mkdir(parents=True)
            (use_cases / "README.md").write_text("# Use Cases\n", encoding="utf-8")
            (domain / "README.md").write_text("# Example\n", encoding="utf-8")
            (domain / "sample.md").write_text("# Sample\n", encoding="utf-8")

            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.check_use_case_inventory_layout()
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        self.assertEqual([], issues)

    def test_use_case_inventory_layout_rejects_nested_use_case_directories(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            domain = use_cases / "example"
            nested = domain / "sample"
            nested.mkdir(parents=True)
            (use_cases / "README.md").write_text("# Use Cases\n", encoding="utf-8")
            (domain / "README.md").write_text("# Example\n", encoding="utf-8")
            (nested / "README.md").write_text("# Sample\n", encoding="utf-8")

            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.check_use_case_inventory_layout()
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        self.assertIn(
            "docs/use-cases/example/sample: use cases must be direct Markdown files",
            "\n".join(issues).replace("\\", "/"),
        )

    def issues_for_use_case(self, callout: str, ac_line: str = "- **AC-001** Works.") -> list[str]:
        matrix = (
            ""
            if "## Acceptance Test Matrix" in callout
            else """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |

"""
        )
        return self.issues_for_document(
            f"""# Sample use case

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
{ac_line}

"""
            + matrix
            + """
## Out Of Scope

- N/A.

"""
            + callout
        )

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
> | Frontend | Not started |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** N/A.
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
> | Frontend | Not started |
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertEqual([], issues)

    def test_rejects_acceptance_criteria_without_bold_id_prefix(self) -> None:
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
""",
            ac_line="- AC-001 Works.",
        )

        self.assertIn("must use `- **AC-001** ...` format", "\n".join(issues))

    def test_rejects_evidence_source_column_in_acceptance_test_matrix(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Evidence source | Verification | Required |
|---|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Main flow | Browser automation | Yes |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertIn("must not include an `Evidence source` column", "\n".join(issues))

    def test_rejects_missing_acceptance_test_matrix_when_required(self) -> None:
        issues = self.issues_for_document(
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
- **AC-001** Works.

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertIn("missing acceptance test matrix section", "\n".join(issues))

    def test_rejects_file_paths_in_acceptance_test_matrix(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | `frontend/e2e/sample.pw.ts` | Yes |
| AT-002 | API boundary | Backend side effect | AC-001 | Axis.Api.Tests | Yes |
| AT-003 | UI component | UI validation | AC-001 | npm run test | Yes |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("contains implementation details", joined)

    def test_accepts_high_level_acceptance_test_matrix(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |
| AT-002 | API boundary | Backend side effect | AC-001 | API integration test | Yes |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertEqual([], issues)

    def complete_use_case_document(self, matrix: str, *, inline_evidence: str = "") -> str:
        return f"""# Sample use case

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
- **AC-001** Works.

## Acceptance Test Matrix

{matrix}

{inline_evidence}
## Out Of Scope

- N/A.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | Done |
> | Application | Done |
> | Infrastructure | Done |
> | API | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** Required AT rows are covered.
>
> **Decisions:** N/A.
"""

    def test_complete_use_case_requires_acceptance_evidence_sidecar(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | User completes flow | AC-001 | UI component test | Yes |"""
            )
        )

        self.assertIn("complete use-case docs must include acceptance evidence sidecar", "\n".join(issues))

    def test_rejects_acceptance_evidence_inside_use_case_spec(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | User completes flow | AC-001 | UI component test | Yes |""",
                inline_evidence="""## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend script test tests/sample.test.tsx` |

""",
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend script test tests/sample.test.tsx` |
""",
            evidence_files=("frontend/tests/sample.test.tsx",),
        )

        self.assertIn("Acceptance Evidence belongs in sidecar", "\n".join(issues))

    def test_accepts_complete_use_case_with_required_sidecar_evidence(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI/API boundaries | User completes flow | AC-001 | UI component test + API integration test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/SampleTests.cs` | `python scripts/axis.py frontend script test tests/sample.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
""",
            evidence_files=(
                "frontend/tests/sample.test.tsx",
                "tests/Api/Axis.Api.Tests/Identity/SampleTests.cs",
            ),
        )

        self.assertEqual([], issues)

    def test_accepts_grouped_at_ids_when_evidence_and_commands_match(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | User completes first state | AC-001 | UI component test | Yes |
| AT-002 | UI component | User completes second state | AC-001 | UI component test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend script test tests/sample.test.tsx` |
""",
            evidence_files=("frontend/tests/sample.test.tsx",),
        )

        self.assertEqual([], issues)

    def test_browser_use_case_evidence_requires_playwright_file(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend script test:e2e -- e2e/sample.pw.ts` |
""",
            evidence_files=("frontend/tests/sample.test.tsx",),
        )

        self.assertIn("Browser automation must reference a committed `frontend/e2e/*.pw.ts` test", "\n".join(issues))

    def test_accepts_infrastructure_test_with_targeted_dotnet_filter(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Infrastructure boundary | Infrastructure behavior is proven | AC-001 | Infrastructure test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Modules/Example/Example.Infrastructure.Tests/SampleTests.cs` | `python scripts/axis.py dotnet test -- --filter FullyQualifiedName~SampleTests` |
""",
            evidence_files=("tests/Modules/Example/Example.Infrastructure.Tests/SampleTests.cs",),
        )

        self.assertEqual([], issues)

    def test_rejects_acceptance_matrix_unknown_and_uncovered_ac_ids(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-999 | Browser automation | No |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("references unknown AC IDs: AC-999", joined)
        self.assertIn("required rows do not cover AC IDs: AC-001", joined)

    def test_rejects_acceptance_matrix_invalid_enum_values(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Smoke | User completes flow | AC-001 | Jest | Required |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        joined = "\n".join(issues)
        self.assertIn("invalid Boundary `Smoke`", joined)
        self.assertIn("invalid Verification `Jest`", joined)
        self.assertIn("Required must be `Yes` or `No`", joined)

    def test_rejects_acceptance_matrix_mixed_id_prefixes(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |
| REG-002 | API boundary | Backend side effect | AC-001 | API integration test | Yes |

> **Implementation status**
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
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertIn("invalid ID `REG-002`", "\n".join(issues))

    def test_rejects_implementation_status_table_schema_drift(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Area | State |
> |------|-------|
> | Frontend | N/A |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** N/A.
>
> **Decisions:** N/A.
"""
        )

        self.assertIn("Implementation status table columns must be exactly", "\n".join(issues))

    def test_strips_implementation_status_for_stock_flow_check(self) -> None:
        before = """# Sample

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

> **Implementation status**
>
> **Gaps vs spec:** old.

## Diagrams
"""
        after = before.replace("> **Gaps vs spec:** old.", "> **Gaps vs spec:** new.\n>\n> **Deferred follow-ups:** N/A.")

        self.assertEqual(
            check_use_case_docs.strip_implementation_status_callouts(before),
            check_use_case_docs.strip_implementation_status_callouts(after),
        )

    def test_changed_content_outside_status_uses_merge_base_for_three_dot_range(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample.md"
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
                    self.assertEqual(args[2], "abc123:docs/use-cases/example/sample.md")
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
                ("git", "diff", "--name-only", "origin/main...HEAD"): "docs/use-cases/example/a.md\n",
                ("git", "diff", "--name-only", "--cached"): "docs/use-cases/example/b.md\n",
                ("git", "diff", "--name-only"): "docs/use-cases/example/c.md\n",
                ("git", "ls-files", "--others", "--exclude-standard"): "docs/use-cases/example/d.md\n",
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
                root / "docs" / "use-cases" / "example" / "a.md",
                root / "docs" / "use-cases" / "example" / "b.md",
                root / "docs" / "use-cases" / "example" / "c.md",
                root / "docs" / "use-cases" / "example" / "d.md",
            ],
            paths,
        )
        self.assertIn(["git", "diff", "--name-only"], calls)
        self.assertIn(["git", "ls-files", "--others", "--exclude-standard"], calls)


class TestFoundationDocsGate(unittest.TestCase):
    def issues_for_foundation(
        self,
        *,
        evidence_doc: str | None = None,
        inline_evidence: str = "",
        evidence_files: tuple[str, ...] = (),
        status_rows: tuple[tuple[str, str], ...] = (("Contract", "Done"), ("Frontend", "Done"), ("Tests", "Done")),
    ) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "foundations" / "app-shell" / "app-frame.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            for evidence_file in evidence_files:
                evidence_path = root / evidence_file
                evidence_path.parent.mkdir(parents=True, exist_ok=True)
                evidence_path.write_text("proof\n", encoding="utf-8")

            status_table = "\n".join(f"> | {layer} | {status} |" for layer, status in status_rows)
            path.write_text(
                f"""# App Frame

## Purpose

Provide an app frame.

## Primary actor

- Signed-in user

## Trigger

- User opens an authenticated route.

## Main flow

1. System renders the route.
2. System renders the frame.

## Alternate / error flows

- Narrow viewport reflows.

## Acceptance Criteria

- **AC-001** Route content renders inside the frame.
- **AC-002** The frame fits supported widths.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Frame renders route content. | AC-001 | UI component test | Yes |
| AT-002 | Browser journey | Desktop and mobile frame avoid overflow. | AC-002 | Browser automation | Yes |

{inline_evidence}
## Out Of Scope

- Product workflows.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
{status_table}
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** N/A.
>
> **Verification:** N/A.
>
> **Decisions:** N/A.
""",
                encoding="utf-8",
            )
            if evidence_doc is not None:
                path.with_name("app-frame.evidence.md").write_text(evidence_doc, encoding="utf-8")

            original_root = check_foundation_docs.ROOT
            original_foundations = check_foundation_docs.FOUNDATIONS
            check_foundation_docs.ROOT = root
            check_foundation_docs.FOUNDATIONS = root / "docs" / "foundations"
            try:
                doc = check_foundation_docs.foundation_document(path)
                issues: list[str] = []
                issues.extend(check_foundation_docs.validate_sections(doc))
                issues.extend(check_foundation_docs.validate_acceptance_contract(doc))
                issues.extend(check_foundation_docs.validate_acceptance_evidence(doc))
                issues.extend(check_foundation_docs.validate_implementation_status(doc))
                return issues
            finally:
                check_foundation_docs.ROOT = original_root
                check_foundation_docs.FOUNDATIONS = original_foundations

    def valid_evidence_doc(self) -> str:
        return """# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend script test src/components/shared/AppShell.test.tsx` |
| AT-002 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/app-frame.pw.ts` |
"""

    def test_complete_foundation_requires_acceptance_evidence(self) -> None:
        issues = self.issues_for_foundation()

        self.assertIn("complete foundation docs must include acceptance evidence sidecar", "\n".join(issues))

    def test_complete_foundation_requires_every_required_at_evidence(self) -> None:
        issues = self.issues_for_foundation(
            evidence_doc="""# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend script test src/components/shared/AppShell.test.tsx` |
""",
            evidence_files=("frontend/src/components/shared/AppShell.test.tsx",),
        )

        self.assertIn("Acceptance Evidence missing required AT IDs: AT-002", "\n".join(issues))

    def test_browser_automation_requires_committed_playwright_evidence(self) -> None:
        issues = self.issues_for_foundation(
            evidence_doc="""# App Frame Evidence

> **Navigation**: [docs/foundations/app-shell/app-frame.md](./app-frame.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend script test src/components/shared/AppShell.test.tsx` |
| AT-002 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend script test:e2e -- e2e/app-frame.pw.ts` |
""",
            evidence_files=("frontend/src/components/shared/AppShell.test.tsx",),
        )

        self.assertIn("Browser automation must reference a committed `frontend/e2e/*.pw.ts` test", "\n".join(issues))

    def test_accepts_complete_foundation_with_required_evidence(self) -> None:
        issues = self.issues_for_foundation(
            evidence_doc=self.valid_evidence_doc(),
            evidence_files=(
                "frontend/src/components/shared/AppShell.test.tsx",
                "frontend/e2e/app-frame.pw.ts",
            ),
        )

        self.assertEqual([], issues)

    def test_allows_pending_foundation_without_evidence(self) -> None:
        issues = self.issues_for_foundation(
            status_rows=(("Contract", "Done"), ("Frontend", "Partial"), ("Tests", "Not started")),
        )

        self.assertNotIn("complete foundation docs must include acceptance evidence sidecar", "\n".join(issues))


class TestDocDriftRatchets(unittest.TestCase):
    def issue_text(self, rows: list[tuple[str, str]]) -> str:
        return "\n".join(axis.doc_drift_added_line_issues(rows))

    def test_rejects_skipped_tests(self) -> None:
        issues = self.issue_text([("tests/ExampleTests.cs", '[Fact(Skip = "later")]')])
        self.assertIn("Skipped test introduced", issues)

    def test_rejects_ensure_created(self) -> None:
        issues = self.issue_text([("tests/Fixture.cs", "await db.Database.EnsureCreatedAsync();")])
        self.assertIn("Database setup must use the owning DbContext migration chain", issues)

    def test_rejects_datetime_now_in_src_or_tests(self) -> None:
        issues = self.issue_text([("src/Example.cs", "var now = DateTime.Now;")])
        self.assertIn("DateTime.Now introduced", issues)

    def test_ignores_todo_in_docs(self) -> None:
        issues = axis.doc_drift_added_line_issues([("docs/example.md", "TODO in docs is not this gate")])
        self.assertEqual([], issues)

    def test_rejects_placeholder_marker_in_frontend_source(self) -> None:
        issues = self.issue_text([("frontend/src/features/example/Example.tsx", "const value = 'placeholder';")])
        self.assertIn("New TODO/FIXME/stub marker introduced", issues)

    def test_accepts_tailwind_placeholder_variant_in_frontend_source(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "frontend/src/components/ui/input.tsx",
                    '"placeholder:text-muted-foreground focus-visible:outline-none"',
                )
            ]
        )
        self.assertEqual([], issues)

    def test_accepts_jsx_placeholder_attribute_in_frontend_source(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "frontend/src/features/auth/components/Example.tsx",
                    '<Input placeholder={t("common.emailAddress")} />',
                )
            ]
        )
        self.assertEqual([], issues)

    def test_rejects_machine_specific_paths_in_docs(self) -> None:
        issues = self.issue_text([("docs/playbooks/local-dev.md", "cd /mnt/d/projects/axis && docker compose up -d")])
        self.assertIn("Machine-specific local path introduced", issues)

    def test_rejects_windows_user_paths_in_docs(self) -> None:
        issues = self.issue_text([("docs/playbooks/local-dev.md", r"C:\Users\phuon\AppData\Local")])
        self.assertIn("Machine-specific local path introduced", issues)

    def test_accepts_placeholder_paths_in_docs(self) -> None:
        issues = axis.doc_drift_added_line_issues([("docs/playbooks/local-dev.md", "cd <repo-root> && python scripts/axis.py local-dev up")])
        self.assertEqual([], issues)

    def test_accepts_standard_doc_navigation(self) -> None:
        issues = axis.doc_navigation_line_issues(
            axis.ROOT / "docs/playbooks/example.md",
            "> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)",
        )

        self.assertEqual([], issues)

    def test_rejects_non_standard_doc_navigation(self) -> None:
        issues = "\n".join(
            axis.doc_navigation_line_issues(
                axis.ROOT / "docs/playbooks/example.md",
                "> **Navigation**: [<- docs](../README.md) | [AGENTS](../../AGENTS.md)",
            )
        )

        self.assertIn("non-standard separators or arrows", issues)
        self.assertIn("navigation link label must be a repo markdown path", issues)

    def test_rejects_raw_docker_compose_commands_in_docs(self) -> None:
        issues = self.issue_text([("docs/playbooks/local-dev.md", "docker compose up -d")])
        self.assertIn("Raw Docker Compose command introduced in docs", issues)

    def documented_issue_text(self, files: dict[str, str]) -> str:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return "\n".join(axis.documented_raw_command_issues(files.keys(), root=root))

    def test_rejects_raw_repo_commands_in_documented_workflows(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": "\n".join(
                    [
                        "# Example",
                        "",
                        "```bash",
                        "dotnet build",
                        "npm run test",
                        "npx -y external-design-agent",
                        "openssl genrsa -out key.pem 2048",
                        "python docs/scripts/render-visuals.py",
                        "```",
                    ]
                ),
            }
        )

        self.assertIn("use `python scripts/axis.py dotnet ...`", issues)
        self.assertIn("use `python scripts/axis.py frontend ...`", issues)
        self.assertIn("use an approved project wrapper", issues)
        self.assertIn("use `python scripts/axis.py local-dev certs`", issues)

    def test_accepts_axis_wrapped_documented_commands(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": "\n".join(
                    [
                        "# Example",
                        "",
                        "```bash",
                        "python scripts/axis.py dotnet build",
                        "python scripts/axis.py frontend test",
                        "python scripts/axis.py local-dev certs",
                        "```",
                    ]
                ),
            }
        )

        self.assertEqual("", issues)

class TestWorkingTreeDiffHelpers(unittest.TestCase):
    def test_module_main_supports_dataclass_scripts(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            scripts_dir = Path(temp)
            (scripts_dir / "example-check.py").write_text(
                """
from dataclasses import dataclass

@dataclass(frozen=True)
class Example:
    value: str

def main() -> int:
    return 0 if Example("ok").value == "ok" else 1
""",
                encoding="utf-8",
            )

            with mock.patch.object(axis, "SCRIPTS", scripts_dir):
                self.assertEqual(0, axis.module_main("example-check.py", []))

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

    def test_changed_paths_since_include_checkpoint_and_worktree_paths(self) -> None:
        outputs = {
            ("diff", "--name-only", "abc123..HEAD"): "docs/follow-up.md\n",
            ("diff", "--name-only", "--cached"): "docs/staged.md\n",
            ("diff", "--name-only"): "docs/unstaged.md\n",
            ("ls-files", "--others", "--exclude-standard"): "docs/untracked.md\n",
        }

        with (
            mock.patch.object(axis, "ref_exists", return_value=True),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
        ):
            paths = axis.changed_paths_since("abc123")

        self.assertEqual(
            ["docs/follow-up.md", "docs/staged.md", "docs/unstaged.md", "docs/untracked.md"],
            paths,
        )

    def test_changed_paths_since_rejects_missing_checkpoint(self) -> None:
        with mock.patch.object(axis, "ref_exists", return_value=False):
            with self.assertRaisesRegex(axis.CheckError, "git ref not found"):
                axis.changed_paths_since("missing")

    def test_verify_scope_prefers_working_tree_paths(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=["scripts/axis.py"]),
            mock.patch.object(axis, "diff_range", return_value="base...HEAD"),
            mock.patch.object(axis, "changed_paths", return_value=["src/Axis.Api/Program.cs"]),
        ):
            scope, paths = axis.verify_scope_paths()

        self.assertEqual("working tree", scope)
        self.assertEqual(["scripts/axis.py"], paths)

    def test_verify_scope_uses_branch_diff_when_working_tree_is_clean(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "diff_range", return_value="base...HEAD"),
            mock.patch.object(axis, "changed_paths", return_value=["docs/README.md"]),
        ):
            scope, paths = axis.verify_scope_paths()

        self.assertEqual("base...HEAD", scope)
        self.assertEqual(["docs/README.md"], paths)

    def test_verify_scope_uses_since_checkpoint(self) -> None:
        with mock.patch.object(axis, "changed_paths_since", return_value=["scripts/axis.py"]):
            scope, paths = axis.verify_scope_paths("abc123")

        self.assertEqual("abc123..HEAD + working tree", scope)
        self.assertEqual(["scripts/axis.py"], paths)

    def test_repo_files_include_tracked_and_untracked_files(self) -> None:
        outputs = {
            ("ls-files", "--cached", "--others", "--exclude-standard"): (
                "docs/tracked.md\n"
                "docs/untracked.md\n"
            ),
        }

        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
        ):
            paths = axis.repo_files()

        self.assertEqual(["docs/tracked.md", "docs/untracked.md"], paths)

    def test_repo_files_include_untracked_files_for_pathspec(self) -> None:
        outputs = {
            ("ls-files", "--cached", "--others", "--exclude-standard", "--", "tests/**/*.csproj"): (
                "tests/Tracked/Axis.Tracked.Domain.Tests/Axis.Tracked.Domain.Tests.csproj\n"
                "tests/New/Axis.New.Domain.Tests/Axis.New.Domain.Tests.csproj\n"
            ),
        }

        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=self.fake_git_run(outputs)),
        ):
            paths = axis.repo_files("tests/**/*.csproj")

        self.assertEqual(
            [
                "tests/Tracked/Axis.Tracked.Domain.Tests/Axis.Tracked.Domain.Tests.csproj",
                "tests/New/Axis.New.Domain.Tests/Axis.New.Domain.Tests.csproj",
            ],
            paths,
        )

    def test_iter_files_uses_repo_visible_paths_for_repo_roots(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            src_root = root / "src"
            visible = src_root / "visible.ts"
            ignored = src_root / "node_modules" / "ignored.ts"
            visible.parent.mkdir(parents=True)
            ignored.parent.mkdir(parents=True)
            visible.write_text("export {};\n", encoding="utf-8")
            ignored.write_text("export {};\n", encoding="utf-8")

            with (
                mock.patch.object(axis_repo, "ROOT", root),
                mock.patch.object(axis_repo, "git_visible_paths_under", return_value=[visible]) as visible_paths,
            ):
                self.assertEqual([visible], list(axis.iter_files(src_root, (".ts",))))

            visible_paths.assert_called_once_with(src_root)

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
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.check_vulnerable_packages())

        self.assertEqual("dotnet", calls[0][0])
        self.assertEqual(str(axis.ROOT / "Axis.sln"), calls[0][2])
        self.assertTrue(Path(calls[0][2]).is_absolute())

    def test_frontend_gate_uses_high_severity_audit_threshold(self) -> None:
        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                return_value=axis.subprocess.CompletedProcess([], 0),
            ) as run_npm,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.check_frontend_vulnerable_packages())

        run_npm.assert_called_once_with(["audit", "--audit-level=high"])

    def test_frontend_gate_propagates_failed_audit(self) -> None:
        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                return_value=axis.subprocess.CompletedProcess([], 1),
            ),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(1, axis.check_frontend_vulnerable_packages())


class TestToolVersionGates(unittest.TestCase):
    def test_run_returns_timeout_result_when_optional_command_hangs(self) -> None:
        with mock.patch.object(
            axis.subprocess,
            "run",
            side_effect=axis.subprocess.TimeoutExpired(["tool", "--version"], 8),
        ):
            result = axis.run(["tool", "--version"], capture=True, check=False, timeout=8)

        self.assertEqual(124, result.returncode)
        self.assertIn("timed out after 8 seconds", result.stderr)

    def test_dotnet_sdk_rejects_global_json_major_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            global_json = Path(temp) / "global.json"
            global_json.write_text(
                '{"sdk":{"version":"9.0.100","rollForward":"latestFeature"}}\n',
                encoding="utf-8",
            )

            with mock.patch.object(axis, "GLOBAL_JSON_PATH", global_json):
                ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("selects .NET SDK 9.x", detail)
        self.assertIn("expected 8.x", detail)

    def test_dotnet_sdk_rejects_wrong_major(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(True, "9.0.100", "/usr/bin/dotnet"),
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("expected .NET SDK 8.x", detail)
        self.assertIn("docs/TECH_STACK.md", detail)

    def test_frontend_toolchain_rejects_wrong_node_major(self) -> None:
        with (
            mock.patch.object(axis, "required_node_major", return_value=(True, "22")),
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(True, "v24.13.0", "/usr/bin/node"),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_frontend_toolchain())

        self.assertIn("expected Node 22.x", stderr.getvalue())
        self.assertIn("frontend/.nvmrc", stderr.getvalue())

    def test_frontend_toolchain_env_resolves_nvm_when_path_lacks_node(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            nvm_root = root / ".nvm"
            older_bin = nvm_root / "versions" / "node" / "v22.1.0" / "bin"
            expected_bin = nvm_root / "versions" / "node" / "v22.23.0" / "bin"
            path_dir = root / "plain-path"
            for bin_dir in (older_bin, expected_bin):
                bin_dir.mkdir(parents=True)
                (bin_dir / "node").write_text("", encoding="utf-8")
                (bin_dir / "npm").write_text("", encoding="utf-8")
            path_dir.mkdir()

            def fake_command_version_line(name: str, *_args: str, env: dict[str, str] | None = None):
                path = env.get("PATH", "") if env else ""
                if path.split(axis.os.pathsep)[0] == str(expected_bin):
                    version = "v22.23.0" if name == "node" else "10.9.8"
                    return True, version, str(expected_bin / name)
                return False, f"{name} not found in PATH", name

            with (
                mock.patch.dict(axis.os.environ, {"NVM_DIR": str(nvm_root), "PATH": str(path_dir)}, clear=True),
                mock.patch.object(axis, "required_node_major", return_value=(True, "22")),
                mock.patch.object(axis, "command_version_line", side_effect=fake_command_version_line),
            ):
                env = axis.frontend_toolchain_env()

        self.assertEqual(str(expected_bin), env["PATH"].split(axis.os.pathsep)[0])

    def test_frontend_toolchain_env_resolves_nvm_windows_when_path_lacks_node(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            nvm_home = root / "nvm"
            expected_dir = nvm_home / "v22.23.0"
            path_dir = root / "plain-path"
            expected_dir.mkdir(parents=True)
            path_dir.mkdir()
            (expected_dir / "node.exe").write_text("", encoding="utf-8")
            (expected_dir / "npm.cmd").write_text("", encoding="utf-8")

            def fake_command_version_line(name: str, *_args: str, env: dict[str, str] | None = None):
                if env is None:
                    return False, f"{name} not found in PATH", name
                path = env.get("PATH", "")
                if path.split(axis.os.pathsep)[0] != str(expected_dir):
                    return False, f"{name} not found in PATH", name
                version = "v22.23.0" if name == "node" else "10.9.8"
                suffix = "node.exe" if name == "node" else "npm.cmd"
                return True, version, str(expected_dir / suffix)

            with (
                mock.patch.object(axis, "_nvm_unix_roots", return_value=[]),
                mock.patch.object(axis, "_nvm_windows_roots", return_value=[nvm_home]),
                mock.patch.object(axis.Path, "home", return_value=root),
                mock.patch.dict(axis.os.environ, {"PATH": str(path_dir)}, clear=True),
                mock.patch.object(axis, "required_node_major", return_value=(True, "22")),
                mock.patch.object(axis, "command_version_line", side_effect=fake_command_version_line),
            ):
                env = axis.frontend_toolchain_env()

        self.assertEqual(str(expected_dir), env["PATH"].split(axis.os.pathsep)[0])

    def test_find_openssl_uses_git_for_windows_usr_bin(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            git_bin = Path(temp) / "Git" / "usr" / "bin"
            git_bin.mkdir(parents=True)
            openssl = git_bin / "openssl.exe"
            openssl.write_text("", encoding="utf-8")

            with (
                mock.patch.object(axis.os, "name", "nt"),
                mock.patch.object(axis.shutil, "which", return_value=None),
                mock.patch.object(axis, "_windows_git_usr_bin_dirs", return_value=[git_bin]),
            ):
                self.assertEqual(str(openssl), axis.find_openssl())

    def test_windows_git_usr_bin_dirs_includes_local_programs(self) -> None:
        localappdata = r"C:\Users\alice\AppData\Local"
        with mock.patch.dict(axis.os.environ, {"LOCALAPPDATA": localappdata}, clear=True):
            dirs = axis._windows_git_usr_bin_dirs()
        expected = Path(localappdata) / "Programs" / "Git" / "usr" / "bin"
        self.assertIn(expected, dirs)

    def test_playwright_chromium_status_reports_missing_browser(self) -> None:
        def fake_run(command: list[str], **_kwargs):
            return axis.subprocess.CompletedProcess(command, 1, stdout="", stderr="/missing/chromium\n")

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
        ):
            ok, detail = axis.playwright_chromium_status({"PATH": "/tmp/node"})

        self.assertFalse(ok)
        self.assertIn("/missing/chromium", detail)
        self.assertIn("python scripts/axis.py frontend install-browsers", detail)

    def test_doctor_warns_when_playwright_chromium_is_missing(self) -> None:
        with (
            mock.patch.object(axis, "find_lychee", return_value="/usr/bin/lychee"),
            mock.patch.object(axis, "lychee_version_status", return_value=(True, "lychee 0.23.0")),
            mock.patch.object(axis, "coderabbit_cli_status", return_value=(True, "coderabbit 0.6.0")),
            mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "8.0.100")),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "node_version_status", return_value=(True, "v22.23.0")),
            mock.patch.object(
                axis,
                "playwright_chromium_status",
                return_value=(False, "missing; run `python scripts/axis.py frontend install-browsers`"),
            ),
            mock.patch.object(axis, "_command_version", return_value=("OK", "/usr/bin/tool")),
            mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
            mock.patch.object(axis, "_docker_info_ok", return_value=True),
            mock.patch.object(axis, "_docker_host_ping_ok", return_value=False),
            mock.patch.object(axis, "_http_ok", return_value=False),
            mock.patch.object(axis, "_wsl_docker_ok", return_value=False),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(0, axis.doctor(axis.argparse.Namespace(strict=True)))

        self.assertIn("[WARN] playwright chromium", stdout.getvalue())
        self.assertEqual("", stderr.getvalue())

    def test_core_doctor_profile_skips_build_local_dev_and_review_tools(self) -> None:
        with (
            mock.patch.object(axis.shutil, "which", return_value="/usr/bin/tool"),
            mock.patch.object(axis, "_command_version", return_value=("OK", "tool 1.0")),
            mock.patch.object(axis, "dotnet_sdk_status") as dotnet,
            mock.patch.object(axis, "frontend_toolchain_env") as frontend,
            mock.patch.object(axis, "_docker_info_ok") as docker,
            mock.patch.object(axis, "find_lychee") as lychee,
            mock.patch.object(axis, "coderabbit_doctor_status") as coderabbit,
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.doctor(axis.argparse.Namespace(profile="core", strict=True)),
            )

        self.assertIn("profile=core", stdout.getvalue())
        for skipped in (dotnet, frontend, docker, lychee, coderabbit):
            skipped.assert_not_called()

    def test_coderabbit_doctor_skips_windows_command_shim_in_non_strict_mode(self) -> None:
        with (
            mock.patch.object(axis.os, "name", "nt"),
            mock.patch.object(axis, "resolve_exe", return_value="coderabbit.cmd"),
            mock.patch.object(axis.shutil, "which", return_value=r"C:\Users\alice\.local\bin\coderabbit.cmd"),
            mock.patch.object(axis, "coderabbit_cli_status") as version_check,
        ):
            status, detail = axis.coderabbit_doctor_status(strict=False)

        self.assertEqual("WARN", status)
        self.assertIn("version check skipped", detail)
        version_check.assert_not_called()


class TestMarkdownLinkGate(unittest.TestCase):
    def test_runs_lychee_with_shared_config(self) -> None:
        calls: list[list[str]] = []

        def fake_run(args: list[str], **_kwargs):
            calls.append(args)
            return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "find_lychee", return_value="/usr/bin/lychee"),
            mock.patch.object(
                axis,
                "run_optional",
                return_value=axis.subprocess.CompletedProcess(
                    ["/usr/bin/lychee", "--version"],
                    0,
                    stdout="lychee 0.23.0\n",
                    stderr="",
                ),
            ),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.check_markdown_links())

        self.assertEqual([["/usr/bin/lychee", "--config", "./lychee.toml", "./**/*.md"]], calls)

    def test_fails_when_lychee_version_is_wrong(self) -> None:
        with (
            mock.patch.object(axis, "find_lychee", return_value="/usr/bin/lychee"),
            mock.patch.object(
                axis,
                "run_optional",
                return_value=axis.subprocess.CompletedProcess(
                    ["/usr/bin/lychee", "--version"],
                    0,
                    stdout="lychee 0.24.2\n",
                    stderr="",
                ),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_markdown_links())

        output = stderr.getvalue()
        self.assertIn("Lychee 0.23.0 is required", output)
        self.assertIn("found `lychee 0.24.2`", output)

    def test_fails_when_lychee_is_missing(self) -> None:
        with (
            mock.patch.object(axis, "find_lychee", return_value=None),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_markdown_links())

        self.assertIn("Lychee 0.23.0 is required", stderr.getvalue())

    def test_coderabbit_cli_rejects_missing_cli(self) -> None:
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(False, "coderabbit not found in PATH", "coderabbit"),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_coderabbit_cli())

        output = stderr.getvalue()
        self.assertIn("CodeRabbit CLI is required", output)
        self.assertIn("docs/playbooks/scripts.md#tool-versions", output)

    def test_coderabbit_cli_rejects_old_version(self) -> None:
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(True, "0.5.9", "/usr/bin/coderabbit"),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_coderabbit_cli())

        self.assertIn("expected version >= 0.6.0", stderr.getvalue())

    def test_coderabbit_cli_accepts_supported_version(self) -> None:
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(True, "0.6.3", "/usr/bin/coderabbit"),
            ),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(0, axis.check_coderabbit_cli())

        self.assertIn("coderabbit-cli: OK", stdout.getvalue())


class TestVerifyGate(unittest.TestCase):
    def test_plan_only_prints_selected_steps_without_running_checks(self) -> None:
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/package-lock.json", "scripts/axis.py"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check") as text_encoding,
            mock.patch.object(axis, "check_frontend_toolchain") as toolchain,
            mock.patch.object(axis, "check_frontend_vulnerable_packages") as audit,
            mock.patch.object(axis, "frontend_command") as frontend,
            mock.patch.object(axis, "check_scripts_standard") as scripts_standard,
            mock.patch.object(axis, "check_policy_tests") as policy_tests,
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.verify(axis.argparse.Namespace(since=None, plan_only=True)),
            )

        output = stdout.getvalue()
        self.assertIn("PLAN frontend vulnerable packages", output)
        self.assertIn("PLAN policy gate tests", output)
        self.assertIn("no commands run", output)
        for patched in (text_encoding, toolchain, audit, frontend, scripts_standard, policy_tests):
            patched.assert_not_called()

    def test_package_manifest_change_runs_frontend_vulnerability_gate(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/package-lock.json"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_vulnerable_packages",
                side_effect=lambda: calls.append("audit") or 0,
            ),
            mock.patch.object(
                axis,
                "frontend_command",
                side_effect=lambda args: calls.append(args.frontend_command) or 0,
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(["toolchain", "audit", "ci", "test"], calls)

    def test_runs_markdown_links_for_markdown_changes(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["docs/README.md"])),
            mock.patch.object(axis, "check_doc_navigation", side_effect=lambda: calls.append("doc-navigation") or 0),
            mock.patch.object(axis, "check_doc_size_budgets", side_effect=lambda: calls.append("doc-size-budgets") or 0),
            mock.patch.object(axis, "run_module_check", side_effect=lambda script, _args: calls.append(script) or 0),
            mock.patch.object(
                axis,
                "check_markdown_links_for_paths",
                side_effect=lambda paths: calls.append(f"markdown-links:{','.join(paths or [])}") or 0,
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "doc-navigation",
                "doc-size-budgets",
                "check-doc-code-fences.py",
                "markdown-links:docs/README.md",
            ],
            calls,
        )


class TestReviewVerificationGates(unittest.TestCase):
    def test_rejects_dirty_worktree_before_running_checks(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=["scripts/axis.py"]),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_ready_review_policy") as policy,
            contextlib.redirect_stderr(io.StringIO()),
        ):
            result = axis.ready_review(axis.argparse.Namespace(since=None, policy_only=False))

        self.assertEqual(1, result)
        verify.assert_not_called()
        policy.assert_not_called()

    def test_runs_verify_and_shared_policy_profile(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["frontend/src/App.tsx"])),
            mock.patch.object(axis, "verify", return_value=0) as verify,
            mock.patch.object(axis, "run_ready_review_policy", return_value=(0, ["doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.ready_review(axis.argparse.Namespace(since=None, policy_only=False))

        self.assertEqual(0, result)
        verify.assert_called_once()
        policy.assert_called_once_with(
            ["frontend/src/App.tsx"],
            policy_tests_covered=False,
            doc_drift_covered=set(),
        )

    def test_policy_only_uses_same_profile_without_verify(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["scripts/axis.py"])),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_ready_review_policy", return_value=(0, ["policy gate tests", "doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.ready_review(axis.argparse.Namespace(since=None, policy_only=True))

        self.assertEqual(0, result)
        verify.assert_not_called()
        policy.assert_called_once_with(
            ["scripts/axis.py"],
            policy_tests_covered=False,
            doc_drift_covered=set(),
        )

    def test_policy_registry_routes_only_triggered_expensive_checks(self) -> None:
        names = [
            name
            for name, _checker in axis.ready_review_policy_gates(
                ["scripts/axis.py", ".github/renovate.json5"]
            )
        ]

        self.assertEqual(["policy gate tests", "Renovate config", "doc drift"], names)
        self.assertEqual(
            ["doc drift"],
            [
                name
                for name, _checker in axis.ready_review_policy_gates(
                    ["frontend/src/App.tsx"],
                    policy_tests_covered=True,
                )
            ],
        )

    def test_ready_review_reuses_verify_coverage_in_doc_drift(self) -> None:
        paths = [
            "scripts/axis.py",
            ".agents/skills/axis-script-scope/SKILL.md",
            "docs/use-cases/example.md",
            "docs/foundations/example.md",
        ]

        self.assertEqual(
            {
                "check-scripts-standard",
                "check-repo-skills",
                "check-doc-navigation",
                "check-doc-size-budgets",
                "check-doc-code-fences.py",
                "check-use-case-docs.py",
                "check-foundation-docs.py",
            },
            axis.ready_review_doc_drift_coverage(paths),
        )

    def test_doc_drift_gate_receives_covered_checks(self) -> None:
        covered = {"check-repo-skills"}
        gates = dict(
            axis.ready_review_policy_gates(
                [".agents/skills/axis-example/SKILL.md"],
                doc_drift_covered=covered,
            )
        )

        with mock.patch.object(axis, "check_doc_drift", return_value=0) as doc_drift:
            self.assertEqual(0, gates["doc drift"]())

        args = doc_drift.call_args.args[0]
        self.assertEqual(covered, args.skip_checkers)

    def test_pre_push_full_delegates_to_ready_review(self) -> None:
        with (
            mock.patch.dict(axis.os.environ, {"AXIS_PRE_PUSH_FULL": "1"}),
            mock.patch.object(axis, "ready_review", return_value=0) as ready_review,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.pre_push(object())

        self.assertEqual(0, result)
        ready_review.assert_called_once()
        delegated = ready_review.call_args.args[0]
        self.assertIsNone(delegated.since)
        self.assertFalse(delegated.policy_only)

    def test_runs_script_checks_for_script_changes(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["scripts/axis.py"])),
            mock.patch.object(axis, "run_text_encoding_check", side_effect=lambda _paths, label: calls.append(label) or 0),
            mock.patch.object(axis, "check_scripts_standard", side_effect=lambda: calls.append("scripts-standard") or 0),
            mock.patch.object(axis, "check_policy_tests", side_effect=lambda: calls.append("policy-tests") or 0),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(["check-text-encoding-changed", "scripts-standard", "policy-tests"], calls)

    def test_runs_frontend_toolchain_before_frontend_commands(self) -> None:
        calls: list[str] = []

        def fake_run(args: list[str], **_kwargs):
            if args[1:3] == ["run", "ci"]:
                calls.append("npm run ci")
            elif args[1:3] == ["run", "test"]:
                calls.append("npm run test")
            else:
                calls.append(" ".join(args[:3]))
            return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/src/App.tsx"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "frontend-toolchain",
                "frontend-toolchain",
                "npm run ci",
                "frontend-toolchain",
                "npm run test",
            ],
            calls,
        )

    def test_runs_only_changed_frontend_test_file_for_test_only_change(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/tests/button.test.tsx"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                side_effect=lambda args: calls.append(" ".join(args)) or axis.subprocess.CompletedProcess(args, 0),
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "frontend-toolchain",
                "frontend-toolchain",
                "run ci",
                "exec vitest run tests/button.test.tsx",
            ],
            calls,
        )

    def test_runs_changed_frontend_e2e_file_for_e2e_only_change(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/e2e/register.pw.ts"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                side_effect=lambda args: calls.append(" ".join(args)) or axis.subprocess.CompletedProcess(args, 0),
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "frontend-toolchain",
                "frontend-toolchain",
                "run ci",
                "run test:e2e -- e2e/register.pw.ts",
            ],
            calls,
        )

    def test_runs_related_dotnet_projects_for_source_change(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["src/Modules/Identity/Axis.Identity.Domain/Aggregates/User.cs"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check", side_effect=lambda _paths, label: calls.append(label) or 0),
            mock.patch.object(axis, "check_dotnet_sdk", side_effect=lambda: calls.append("dotnet-sdk") or 0),
            mock.patch.object(axis, "dotnet_build_projects", side_effect=lambda projects: calls.append(f"build:{','.join(projects)}") or 0),
            mock.patch.object(axis, "dotnet_format_changed_paths", side_effect=lambda _paths: calls.append("dotnet-format-changed") or 0),
            mock.patch.object(axis, "dotnet_test_projects", side_effect=lambda projects: calls.append(f"test:{','.join(projects)}") or 0),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "check-text-encoding-changed",
                "dotnet-sdk",
                "build:src/Modules/Identity/Axis.Identity.Domain/Axis.Identity.Domain.csproj",
                "dotnet-format-changed",
                "test:tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj,"
                "tests/Modules/Identity/Axis.Identity.Domain.Tests/Axis.Identity.Domain.Tests.csproj",
            ],
            calls,
        )

    def test_discovers_related_test_projects_for_every_module_layer(self) -> None:
        source_projects = sorted((axis.ROOT / "src" / "Modules").glob("*/*/*.csproj"))
        mapped_projects: dict[str, str | None] = {}

        for source_project in source_projects:
            relative_source = axis.rel(source_project)
            mapped_projects[relative_source] = axis.related_test_project_for_source_project(relative_source)

        for source_project, mapped_project in mapped_projects.items():
            project_name = Path(source_project).stem
            module_name = Path(source_project).parts[2]
            expected = (
                axis.ROOT
                / "tests"
                / "Modules"
                / module_name
                / f"{project_name}.Tests"
                / f"{project_name}.Tests.csproj"
            )
            self.assertEqual(axis.rel(expected) if expected.is_file() else None, mapped_project, source_project)

        self.assertIn(
            "tests/Modules/BusinessObjects/Axis.BusinessObjects.Application.Tests/Axis.BusinessObjects.Application.Tests.csproj",
            mapped_projects.values(),
        )
        self.assertIn(
            "tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj",
            mapped_projects.values(),
        )



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


class TestEnforcementLedger(unittest.TestCase):
    def issues_for_enforcement_ledger(self, ledger_rows: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "ENFORCEMENT.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# Enforcement

## Ledger

| Finding class | Rule owner | Trigger / scope | Mechanism | Proof / gap | Status |
|---|---|---|---|---|---|
"""
                + ledger_rows,
                encoding="utf-8",
            )
            return axis.enforcement_ledger_issues(root=root)

    def test_rejects_missing_rule_owner(self) -> None:
        issues = self.issues_for_enforcement_ledger(
            "| Example finding |  | PR scope | CI job | negative test | **Enforced** |\n"
        )

        self.assertIn("Rule owner", "\n".join(issues))

    def test_rejects_unknown_status(self) -> None:
        issues = self.issues_for_enforcement_ledger(
            "| Example finding | This file | PR scope | Review | Human review | **Mandatory** |\n"
        )

        self.assertIn("unknown ledger status", "\n".join(issues))

    def test_rejects_partial_without_known_gap(self) -> None:
        issues = self.issues_for_enforcement_ledger(
            "| Example finding | This file | PR scope | Diff-based check | Unchanged files are not swept | **Partial** |\n"
        )

        self.assertIn("Partial row must name a known gap", "\n".join(issues))

    def test_rejects_review_only_gate_language(self) -> None:
        issues = self.issues_for_enforcement_ledger(
            "| Example finding | This file | PR scope | CI gate | Human review | **Review-only** |\n"
        )

        self.assertIn("must not use gate/enforced language", "\n".join(issues))

    def test_current_repository_enforcement_ledger_still_passes(self) -> None:
        self.assertEqual([], axis.enforcement_ledger_issues())


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
            files[workflow] = files[workflow].replace(
                "run: python scripts/axis.py ready-review --policy-only\n",
                "",
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("shared ready-review policy profile runs in CI", "\n".join(issues))

    def test_rejects_ci_without_frontend_vulnerability_gate(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            workflow = Path(".github/workflows/build-and-test.yml")
            files[workflow] = files[workflow].replace(
                "run: python scripts/axis.py check frontend-vulnerable-packages\n",
                "",
            )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("frontend dependency vulnerability gate runs in CI", "\n".join(issues))

    def test_rejects_local_verify_without_markdown_links(self) -> None:
        def mutate(files: dict[Path, str]) -> None:
            script = Path("scripts/axis.py")
            files[script] = files[script].replace('step("markdown links (changed files)",\n', "")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("local verify runs markdown link check", "\n".join(issues))

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

    def test_check_text_encoding_rejects_untracked_utf8_bom(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "untracked.md"
            path.parent.mkdir(parents=True)
            path.write_bytes(b"\xef\xbb\xbf# Title\n")

            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "repo_files", return_value=["docs/untracked.md"]),
                contextlib.redirect_stdout(io.StringIO()),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                rc = axis.check_text_encoding()

        self.assertEqual(1, rc)
        self.assertIn("docs/untracked.md: UTF-8 BOM found", stderr.getvalue())


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
            {"docs/playbooks/patterns.md": "\n".join("line" for _ in range(101))}
        )

        self.assertIn("100-line docs budget", "\n".join(issues))

    def test_rejects_overlong_playbook(self) -> None:
        issues = self.issues_for_files(
            {"docs/playbooks/api-patterns.md": "\n".join("line" for _ in range(101))}
        )

        self.assertIn("100-line docs budget", "\n".join(issues))

    def test_current_repository_doc_size_budgets_still_pass(self) -> None:
        self.assertEqual([], axis.doc_size_budget_issues())


class TestScriptsStandardGate(unittest.TestCase):
    def issues_for_files(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.non_python_utility_script_issues(root=root)

    def test_rejects_executable_top_level_python_script(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            root = Path(temp)
            script = root / "scripts" / "check-local-dev-docs.py"
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_text("#!/usr/bin/env python3\nprint('ok')\n", encoding="utf-8")

            original_stat = axis.Path.stat

            def fake_stat(path: Path, *args, **kwargs):
                result = original_stat(path, *args, **kwargs)
                if path == script:
                    return axis.os.stat_result((result.st_mode | 0o111, *result[1:]))
                return result

            with mock.patch.object(axis.os, "name", "posix"), mock.patch.object(axis.Path, "stat", fake_stat):
                issues = axis.non_python_utility_script_issues(root=root)

        self.assertIn(
            "scripts/check-local-dev-docs.py: top-level Python scripts must not be executable; "
            "run them through scripts/axis.py",
            issues,
        )

    def test_rejects_non_python_docs_utility_script(self) -> None:
        issues = self.issues_for_files({"docs/scripts/render-visuals.mjs": "console.log('nope');\n"})
        self.assertIn(
            "docs/scripts/render-visuals.mjs: docs-level utility scripts must be Python; "
            "native tooling belongs beside its owning package",
            issues,
        )

    def test_rejects_non_python_docs_utility_script_case_insensitive(self) -> None:
        issues = self.issues_for_files({"docs/scripts/render-visuals.MJS": "console.log('nope');\n"})
        self.assertIn(
            "docs/scripts/render-visuals.MJS: docs-level utility scripts must be Python; "
            "native tooling belongs beside its owning package",
            issues,
        )

    def test_accepts_python_docs_utility_native_frontend_tooling_and_visual_assets(self) -> None:
        issues = self.issues_for_files(
            {
                "docs/scripts/render-visuals.py": "print('ok')\n",
                "frontend/package.json": '{"scripts":{"export:visuals":"node scripts/export-visuals.mjs"}}\n',
                "frontend/scripts/export-visuals.mjs": "console.log('native package tooling');\n",
            }
        )
        self.assertEqual([], issues)

    def test_rejects_non_python_pre_push_hook(self) -> None:
        issues = self.issues_for_files(
            {"scripts/hooks/pre-push": "#!/usr/bin/env bash\npython scripts/axis.py pre-push\n"}
        )
        self.assertIn("scripts/hooks/pre-push: pre-push hook must be a Python entrypoint", issues)

    def test_rejects_executable_pre_push_hook_source(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            root = Path(temp)
            hook = root / "scripts" / "hooks" / "pre-push"
            hook.parent.mkdir(parents=True, exist_ok=True)
            hook.write_text(
                "#!/usr/bin/env python3\n"
                'os.execv(sys.executable, [sys.executable, str(root / "scripts" / "axis.py"), "pre-push"])\n',
                encoding="utf-8",
            )

            original_stat = axis.Path.stat

            def fake_stat(path: Path, *args, **kwargs):
                result = original_stat(path, *args, **kwargs)
                if path == hook:
                    return axis.os.stat_result((result.st_mode | 0o111, *result[1:]))
                return result

            with mock.patch.object(axis.os, "name", "posix"), mock.patch.object(axis.Path, "stat", fake_stat):
                issues = axis.non_python_utility_script_issues(root=root)

        self.assertIn(
            "scripts/hooks/pre-push: committed hook source must not be executable; "
            "install-hooks writes the executable copy under .git/hooks",
            issues,
        )

    def test_current_repository_scripts_standard_still_passes(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_scripts_standard())


class TestLocalDevCli(unittest.TestCase):
    def test_local_dev_docs_service_match_requires_token_boundaries(self) -> None:
        doc = "Mandatory services: `api`; optional service: `otel-lgtm`."

        self.assertTrue(check_local_dev_docs.mentions_service(doc, "api"))
        self.assertTrue(check_local_dev_docs.mentions_service(doc, "otel-lgtm"))
        self.assertFalse(check_local_dev_docs.mentions_service("application apiary", "api"))
        self.assertFalse(check_local_dev_docs.mentions_service("otel-lgtms", "otel-lgtm"))

    def test_parse_compose_reports_services_without_ports(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            compose = Path(temp) / "docker-compose.yml"
            compose.write_text(
                "services:\n"
                "  api:\n"
                "    image: axis-api\n"
                "    ports:\n"
                "      - \"127.0.0.1:5281:8443\"\n"
                "  worker:\n"
                "    image: axis-worker\n"
                "  e2e:\n"
                "    profiles: [\"e2e\"]\n"
                "    image: axis-e2e\n"
                "volumes:\n"
                "  data:\n",
                encoding="utf-8",
            )

            services, optional, service_names = check_local_dev_docs.parse_compose(compose)

        self.assertEqual({"api": [5281]}, services)
        self.assertEqual({"e2e"}, optional)
        self.assertEqual(["api", "e2e", "worker"], service_names)

    def test_compose_app_base_url_allows_human_local_dev_default(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            compose = Path(temp) / "docker-compose.yml"
            compose.write_text(
                "services:\n"
                "  api:\n"
                "    environment:\n"
                "      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"\n",
                encoding="utf-8",
            )

            self.assertTrue(check_local_dev_docs.compose_has_local_app_base_url(compose))

    def test_compose_app_base_url_rejects_internal_service_origin(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            compose = Path(temp) / "docker-compose.yml"
            compose.write_text(
                "services:\n"
                "  api:\n"
                "    environment:\n"
                "      App__BaseUrl: \"https://web:3000\"\n",
                encoding="utf-8",
            )

            self.assertFalse(check_local_dev_docs.compose_has_local_app_base_url(compose))

    def test_api_appsettings_base_url_reads_app_base_url(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            appsettings = Path(temp) / "appsettings.json"
            appsettings.write_text(
                '{"App": {"BaseUrl": "https://localhost:3000"}}',
                encoding="utf-8",
            )

            self.assertEqual(
                "https://localhost:3000",
                check_local_dev_docs.api_appsettings_base_url(appsettings),
            )

    def run_local_dev(
        self,
        args: axis.argparse.Namespace,
        *,
        env_file: Path | None = None,
    ) -> list[list[str]]:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        temp_dir: tempfile.TemporaryDirectory[str] | None = None
        if env_file is None:
            temp_dir = tempfile.TemporaryDirectory()
            env_file = Path(temp_dir.name) / ".env.local"

        with (
            temp_dir if temp_dir is not None else contextlib.nullcontext(),
            mock.patch.object(axis, "LOCAL_DEV_ENV_FILE", env_file),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.local_dev(args))

        return calls

    def test_up_uses_axis_project_and_committed_compose_file(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="up", build=False, services=[])
        )

        self.assertEqual(
            ["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "up", "-d"],
            calls[0][1:],
        )

    def test_up_uses_local_env_file_when_present(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            env_file = Path(temp) / ".env.local"
            env_file.write_text("AXIS_LOCAL_TEST=1\n", encoding="utf-8")

            calls = self.run_local_dev(
                axis.argparse.Namespace(local_dev_command="up", build=False, services=[]),
                env_file=env_file,
            )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "--env-file",
                str(env_file),
                "up",
                "-d",
            ],
            calls[0][1:],
        )

    def test_e2e_builds_and_runs_profile(self) -> None:
        calls = self.run_local_dev(axis.argparse.Namespace(local_dev_command="e2e", e2e_args=[]))

        self.assertEqual(
            ["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "--profile", "e2e", "build", "e2e"],
            calls[0][1:],
        )
        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "--profile",
                "e2e",
                "run",
                "--rm",
                "--no-deps",
                "e2e",
            ],
            calls[1][1:],
        )

    def test_e2e_forwards_playwright_args(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="e2e",
                e2e_args=["--", "e2e/sign-in-user.pw.ts", "-g", "AT-001"],
            )
        )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "--profile",
                "e2e",
                "run",
                "--rm",
                "--no-deps",
                "e2e",
                "e2e/sign-in-user.pw.ts",
                "-g",
                "AT-001",
            ],
            calls[1][1:],
        )

    def test_smoke_runs_host_playwright_against_running_local_stack(self) -> None:
        calls: list[list[str]] = []
        envs: list[dict[str, str] | None] = []

        def fake_run(command: list[str], **kwargs):
            calls.append(command)
            envs.append(kwargs.get("env"))
            if command[1:] == [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "ps",
                "--services",
                "--status",
                "running",
            ]:
                return axis.subprocess.CompletedProcess(command, 0, stdout="api\nweb\n", stderr="")
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.pem"
            root_ca.write_text("test root ca", encoding="utf-8")
            browser_home = Path(temp) / "browser-home"

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", root_ca),
                mock.patch.object(axis, "LOCAL_DEV_BROWSER_HOME", browser_home),
                mock.patch.object(axis, "_docker_compose_ok", return_value=True),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(axis, "playwright_chromium_status", return_value=(True, "chromium")),
                mock.patch.object(axis, "ensure_local_dev_smoke_browser_trust", return_value=0),
                mock.patch.object(axis, "frontend_toolchain_env", return_value={"PATH": "node-bin"}),
                mock.patch.object(axis, "run", side_effect=fake_run),
            ):
                result = axis.local_dev(
                    axis.argparse.Namespace(
                        local_dev_command="smoke",
                        smoke_args=["--", "e2e/app-frame.pw.ts", "-g", "AT-002"],
                    )
                )

        self.assertEqual(0, result)
        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "ps",
                "--services",
                "--status",
                "running",
            ],
            calls[0][1:],
        )
        self.assertEqual(["run", "test:e2e", "--", "e2e/app-frame.pw.ts", "-g", "AT-002"], calls[1][1:])
        self.assertEqual("https://localhost:5281", envs[1]["E2E_API_URL"])
        self.assertEqual("https://localhost:3000", envs[1]["E2E_BASE_URL"])
        self.assertEqual(str(browser_home), envs[1]["E2E_BROWSER_HOME"])
        self.assertEqual("1", envs[1]["E2E_SKIP_WEB_SERVER"])
        self.assertEqual("https://localhost:3000", envs[1]["E2E_VERIFY_ORIGIN"])
        self.assertEqual(str(root_ca), envs[1]["NODE_EXTRA_CA_CERTS"])

    def test_smoke_browser_trust_imports_root_ca_into_isolated_nss_db(self) -> None:
        calls: list[list[str]] = []

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.pem"
            root_ca.write_text("test root ca", encoding="utf-8")
            browser_home = Path(temp) / "browser-home"

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
                if "run" in command:
                    nss_db = browser_home / ".pki" / "nssdb"
                    nss_db.mkdir(parents=True, exist_ok=True)
                    (nss_db / "cert9.db").write_text("trusted", encoding="utf-8")
                return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", root_ca),
                mock.patch.object(axis, "LOCAL_DEV_BROWSER_HOME", browser_home),
                mock.patch.object(axis, "run", side_effect=fake_run),
            ):
                result = axis.ensure_local_dev_smoke_browser_trust()

            self.assertEqual(0, result)
            self.assertEqual(2, len(calls))
            self.assertEqual(
                ["--profile", "e2e", "build", "e2e"],
                calls[0][-4:],
            )
            self.assertIn(f"{browser_home}:/browser-home", calls[1])
            self.assertTrue((browser_home / ".axis-root-ca.sha256").is_file())

    def test_smoke_fails_when_required_local_services_are_not_running(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="api\n", stderr="")

        stderr = io.StringIO()
        with (
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "playwright_chromium_status", return_value=(True, "chromium")),
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis.sys, "stderr", stderr),
        ):
            result = axis.local_dev(axis.argparse.Namespace(local_dev_command="smoke", smoke_args=[]))

        self.assertEqual(1, result)
        self.assertEqual(1, len(calls))
        self.assertIn("required services are not running: web", stderr.getvalue())

    def test_shell_uses_service_default_inside_container(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="shell", service="web", exec_command=[])
        )

        self.assertEqual(
            ["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "exec", "-it", "web", "sh"],
            calls[0][1:],
        )

    def test_shell_honors_explicit_command(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="shell",
                service="api",
                exec_command=["bash", "-lc", "dotnet --version"],
            )
        )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "exec",
                "-it",
                "api",
                "bash",
                "-lc",
                "dotnet --version",
            ],
            calls[0][1:],
        )

    def test_observability_up_starts_lgtm_profile(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="observability", observability_command="up")
        )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "--profile",
                "observability",
                "up",
                "-d",
                "otel-lgtm",
            ],
            calls[0][1:],
        )

    def test_reset_db_removes_postgres_volume_between_down_and_up(self) -> None:
        calls = self.run_local_dev(axis.argparse.Namespace(local_dev_command="reset-db"))

        self.assertEqual(["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "down"], calls[0][1:])
        self.assertEqual(["volume", "rm", "axis_postgres_data"], calls[1][1:])
        self.assertEqual(["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "up", "-d"], calls[2][1:])

    def test_reset_db_fails_when_postgres_volume_removal_fails(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            if command[1:3] == ["volume", "rm"]:
                return axis.subprocess.CompletedProcess(command, 1, stdout="", stderr="permission denied")
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
            tempfile.TemporaryDirectory() as temp,
            mock.patch.object(axis, "LOCAL_DEV_ENV_FILE", Path(temp) / ".env.local"),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(1, axis.local_dev(axis.argparse.Namespace(local_dev_command="reset-db")))

        self.assertEqual(2, len(calls))

    def test_reset_db_continues_when_postgres_volume_is_absent(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            if command[1:3] == ["volume", "rm"]:
                return axis.subprocess.CompletedProcess(command, 1, stdout="", stderr="No such volume: axis_postgres_data")
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
            tempfile.TemporaryDirectory() as temp,
            mock.patch.object(axis, "LOCAL_DEV_ENV_FILE", Path(temp) / ".env.local"),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.local_dev(axis.argparse.Namespace(local_dev_command="reset-db")))

        self.assertEqual(["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "up", "-d"], calls[2][1:])


class TestLocalDevShellArgv(unittest.TestCase):
    def test_defaults_by_service(self) -> None:
        self.assertEqual(["bash"], axis.local_dev_shell_argv("api", []))
        self.assertEqual(["sh"], axis.local_dev_shell_argv("web", []))
        self.assertEqual(["sh"], axis.local_dev_shell_argv("unknown", []))

    def test_strips_double_dash_prefix(self) -> None:
        self.assertEqual(["bash"], axis.local_dev_shell_argv("web", ["--", "bash"]))


class TestAxisCommandWrappers(unittest.TestCase):
    def run_with_fake_process(self, func, args: axis.argparse.Namespace) -> list[list[str]]:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
            mock.patch.object(axis.shutil, "which", return_value="/usr/bin/tool"),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, func(args))

        return calls

    def test_dotnet_build_uses_solution_wrapper(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="build", dotnet_args=["--no-restore"]),
        )

        self.assertEqual(["dotnet", "build", "Axis.sln", "--nologo", "--no-restore"], calls[0])

    def test_setup_restores_locked_dependencies_and_optional_browser(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0)

        def fake_frontend(args: list[str]):
            calls.append(["npm", *args])
            return axis.subprocess.CompletedProcess(args, 0)

        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "run_frontend_npm", side_effect=fake_frontend),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.setup(axis.argparse.Namespace(browsers=True)))

        self.assertEqual(
            [
                ["dotnet", "restore", "Axis.sln"],
                ["npm", "ci"],
                ["npm", "exec", "--", "playwright", "install", "chromium"],
            ],
            calls,
        )

    def test_setup_fails_before_mutating_when_a_toolchain_is_missing(self) -> None:
        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=1),
            mock.patch.object(axis, "check_frontend_toolchain") as frontend_check,
            mock.patch.object(axis, "run") as run,
            mock.patch.object(axis, "run_frontend_npm") as run_npm,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(1, axis.setup(axis.argparse.Namespace(browsers=False)))

        frontend_check.assert_not_called()
        run.assert_not_called()
        run_npm.assert_not_called()

    def test_dotnet_build_strips_argparse_separator(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="build", dotnet_args=["--", "--no-restore"]),
        )

        self.assertEqual(["dotnet", "build", "Axis.sln", "--nologo", "--no-restore"], calls[0])

    def test_dotnet_test_uses_solution_by_default(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="test", dotnet_args=["--", "--no-build"]),
        )

        self.assertEqual(["dotnet", "test", "Axis.sln", "--nologo", "--no-build"], calls[0])

    def test_dotnet_test_accepts_project_target(self) -> None:
        project = "tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj"
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(
                dotnet_command="test",
                dotnet_args=[project, "--", "--filter", "FullyQualifiedName~CreateRuleDefinitionHandlerTests"],
            ),
        )

        self.assertEqual(
            [
                "dotnet",
                "test",
                project,
                "--nologo",
                "--filter",
                "FullyQualifiedName~CreateRuleDefinitionHandlerTests",
            ],
            calls[0],
        )

    def test_dotnet_format_check_uses_verify_no_changes(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="format", check=True, dotnet_args=[]),
        )

        self.assertEqual(["dotnet", "format", "Axis.sln", "--verify-no-changes"], calls[0])

    def test_frontend_gen_api_types_check_generates_without_diffing_head(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(frontend_command="gen-api-types", check=True),
        )

        self.assertEqual(["npm", "run", "gen:api-types"], calls[0])
        self.assertEqual(1, len(calls))

    def test_frontend_gen_api_types_check_restores_stale_working_file(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            frontend = Path(temp)
            generated = frontend / "src" / "lib" / "api-types.ts"
            generated.parent.mkdir(parents=True)
            generated.write_text("current", encoding="utf-8")

            def generate(_args: list[str]) -> subprocess.CompletedProcess[str]:
                generated.write_text("generated", encoding="utf-8")
                return subprocess.CompletedProcess([], 0)

            with (
                mock.patch.object(axis, "FRONTEND_DIR", frontend),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(axis, "run_frontend_npm", side_effect=generate),
                contextlib.redirect_stderr(io.StringIO()),
            ):
                rc = axis.frontend_command(
                    axis.argparse.Namespace(frontend_command="gen-api-types", check=True)
                )

            self.assertEqual(1, rc)
            self.assertEqual("current", generated.read_text(encoding="utf-8"))

    def test_frontend_command_runs_npm_with_resolved_frontend_env(self) -> None:
        calls: list[dict[str, dict[str, str] | None]] = []
        frontend_env = {"PATH": "/tmp/nvm-node-bin:/usr/bin"}

        def fake_run(command: list[str], **kwargs):
            calls.append({"env": kwargs.get("env")})
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value=frontend_env),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.frontend_command(axis.argparse.Namespace(frontend_command="ci")))

        self.assertEqual(frontend_env, calls[0]["env"])

    def test_frontend_ui_baseline_write_uses_deterministic_python_generator(self) -> None:
        with (
            mock.patch.object(axis, "write_ui_baseline") as write_baseline,
            mock.patch.object(axis, "check_frontend_toolchain") as check_toolchain,
        ):
            rc = axis.frontend_command(
                axis.argparse.Namespace(frontend_command="ui-baseline", write=True)
            )

        self.assertEqual(0, rc)
        write_baseline.assert_called_once_with()
        check_toolchain.assert_not_called()

    def test_frontend_install_browsers_installs_playwright_chromium(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(frontend_command="install-browsers"),
        )

        self.assertEqual(["npm", "exec", "--", "playwright", "install", "chromium"], calls[0])

    def test_local_dev_certs_writes_extension_and_runs_openssl(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            cert_dir = Path(temp) / ".dev-certs"
            calls: list[list[str]] = []

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
                if "-out" in command:
                    Path(command[command.index("-out") + 1]).write_text("generated\n", encoding="utf-8")
                return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

            with (
                mock.patch.object(axis, "LOCAL_CERT_DIR", cert_dir),
                mock.patch.object(axis, "LOCAL_ROOT_CA_KEY", cert_dir / "rootCA-key.pem"),
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", cert_dir / "rootCA.pem"),
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", cert_dir / "rootCA.cer"),
                mock.patch.object(axis, "LOCALHOST_KEY", cert_dir / "localhost-key.pem"),
                mock.patch.object(axis, "LOCALHOST_CSR", cert_dir / "localhost.csr"),
                mock.patch.object(axis, "LOCALHOST_EXT", cert_dir / "localhost.ext"),
                mock.patch.object(axis, "LOCALHOST_CERT", cert_dir / "localhost.pem"),
                mock.patch.object(axis, "run", side_effect=fake_run),
                mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
                mock.patch.object(axis.os, "name", "posix"),
                mock.patch.object(axis.Path, "chmod", autospec=True) as chmod,
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.local_dev_certs())

            self.assertTrue((cert_dir / "localhost.ext").is_file())
            self.assertIn("subjectAltName=@alt_names", (cert_dir / "localhost.ext").read_text(encoding="utf-8"))
            chmod.assert_any_call(cert_dir, 0o700)
            chmod.assert_any_call(cert_dir / "rootCA-key.pem", 0o600)
            chmod.assert_any_call(cert_dir / "localhost-key.pem", 0o600)
            self.assertEqual("/usr/bin/openssl", calls[0][0])


class TestInstallHooks(unittest.TestCase):
    def test_cli_exposes_install_hooks(self) -> None:
        with (
            mock.patch.object(axis, "install_hooks", return_value=0) as install_hooks,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(0, axis.main(["install-hooks"]))

        install_hooks.assert_called_once()

    def test_refuses_to_overwrite_custom_core_hooks_path(self) -> None:
        calls: list[list[str]] = []

        def fake_run(args: list[str], **_kwargs):
            calls.append(args)
            if args[1:] == ["config", "--get", "core.hooksPath"]:
                return axis.subprocess.CompletedProcess(args, 0, stdout="custom/hooks\n", stderr="")
            raise AssertionError(f"unexpected command: {args}")

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.install_hooks())

        self.assertIn("refusing to overwrite existing core.hooksPath", stderr.getvalue())
        self.assertEqual([["git", "config", "--get", "core.hooksPath"]], calls)

    def test_replaces_repo_core_hooks_path_with_git_hook_copy(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            target = temp_root / ".git" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("#!/usr/bin/env python3\nprint('pre-push')\n", encoding="utf-8")

            calls: list[list[str]] = []

            def fake_run(args: list[str], **_kwargs):
                calls.append(args)
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout="scripts/hooks\n", stderr="")
                if args[1:] == ["config", "--unset-all", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")
                if args[1:] == ["rev-parse", "--git-path", "hooks/pre-push"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout=f"{target}\n", stderr="")
                raise AssertionError(f"unexpected command: {args}")

            original_root = axis.ROOT
            axis.ROOT = temp_root
            try:
                with (
                    mock.patch.object(axis, "run", side_effect=fake_run),
                    mock.patch.object(axis, "exe", side_effect=lambda name: name),
                    contextlib.redirect_stdout(io.StringIO()),
                ):
                    self.assertEqual(0, axis.install_hooks())
            finally:
                axis.ROOT = original_root

            self.assertEqual(source.read_text(encoding="utf-8"), target.read_text(encoding="utf-8"))
            if axis.os.name != "nt":
                self.assertNotEqual(0, target.stat().st_mode & 0o111)
            self.assertIn(["git", "config", "--unset-all", "core.hooksPath"], calls)


class TestRepoSkillsGate(unittest.TestCase):
    def issues_for_skill(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.repo_skill_issues(root=root)

    def valid_skill_files(self) -> dict[str, str]:
        return {
            ".agents/skills/README.md": (
                "# Skills\n\n"
                "| Intent | Owner |\n"
                "|---|---|\n"
                "| Example | [axis-example/SKILL.md](./axis-example/SKILL.md) |\n"
            ),
            ".agents/skills/reference.md": "# Contract\n",
            ".agents/skills/axis-example/SKILL.md": (
                "---\n"
                "name: axis-example\n"
                "description: Use when an agent needs to perform a concrete Axis example workflow with repo-specific checks.\n"
                "---\n"
                "\n"
                "# Axis Example\n"
                "\n"
                "## Goal\n\nRun the example workflow.\n\n"
                "## Hard gates\n\nFollow [reference.md](../reference.md).\n\n"
                "## Inputs\n\n- Example input.\n\n"
                "## Workflow\n\n1. Perform the example.\n\n"
                "## Output\n\nReport the result.\n"
            ),
        }

    def test_accepts_valid_repo_skill(self) -> None:
        self.assertEqual([], self.issues_for_skill(self.valid_skill_files()))

    def test_rejects_legacy_vendor_adapter_directory(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/agents/openai.yaml"] = (
            "interface:\n"
            "  display_name: \"Axis Example\"\n"
        )

        issues = self.issues_for_skill(files)

        self.assertIn("remove legacy agents/ vendor metadata", "\n".join(issues))

    def test_accepts_reference_to_known_skill_alias(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-other/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", "axis-other").replace("Axis Example", "Axis Other")
        files[".agents/skills/README.md"] += (
            "| Other | [axis-other/SKILL.md](./axis-other/SKILL.md) |\n"
        )
        files[".agents/skills/axis-example/SKILL.md"] += "\nPlain link: `$axis-other`.\n"

        self.assertEqual([], self.issues_for_skill(files))

    def test_rejects_recursive_required_handoffs(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-other/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", "axis-other").replace("Axis Example", "Axis Other")
        files[".agents/skills/README.md"] += (
            "| Other | [axis-other/SKILL.md](./axis-other/SKILL.md) |\n"
        )
        files[".agents/skills/axis-example/SKILL.md"] += (
            "\n- **Requires** `$axis-other` before continuing.\n"
        )
        files[".agents/skills/axis-other/SKILL.md"] += (
            "\n- **Requires** `$axis-example` before continuing.\n"
        )

        self.assertIn("recursive **Requires** handoff", "\n".join(self.issues_for_skill(files)))

    def test_allows_delegate_and_return_handoffs(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-other/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", "axis-other").replace("Axis Example", "Axis Other")
        files[".agents/skills/README.md"] += (
            "| Other | [axis-other/SKILL.md](./axis-other/SKILL.md) |\n"
        )
        files[".agents/skills/axis-example/SKILL.md"] += (
            "\n- **Delegates** to `$axis-other` and waits for evidence.\n"
        )
        files[".agents/skills/axis-other/SKILL.md"] += (
            "\n- **Returns to** `$axis-example` without restarting it.\n"
        )

        self.assertEqual([], self.issues_for_skill(files))

    def test_rejects_unknown_skill_alias(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += "\nDelegate to `$axis-missing`.\n"

        self.assertIn("unknown skill alias `$axis-missing`", "\n".join(self.issues_for_skill(files)))

    def test_rejects_untyped_skill_handoff(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-other/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", "axis-other").replace("Axis Example", "Axis Other")
        files[".agents/skills/README.md"] += (
            "| Other | [axis-other/SKILL.md](./axis-other/SKILL.md) |\n"
        )
        files[".agents/skills/axis-example/SKILL.md"] += "\nDelegate to `$axis-other`.\n"

        self.assertIn("type the skill handoff", "\n".join(self.issues_for_skill(files)))

    def test_rejects_missing_catalog_entry(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/README.md"] = "# Skills\n"

        self.assertIn("missing responsibility entry", "\n".join(self.issues_for_skill(files)))

    def test_rejects_duplicate_catalog_entry(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/README.md"] += (
            "[duplicate](./axis-example/SKILL.md)\n"
        )

        self.assertIn("exactly one responsibility entry", "\n".join(self.issues_for_skill(files)))

    def test_rejects_missing_universal_contract_link(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("Follow [reference.md](../reference.md).", "Follow the contract.")

        self.assertIn("must link the universal", "\n".join(self.issues_for_skill(files)))

    def test_rejects_missing_required_section(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("## Output", "## Result")

        self.assertIn("missing required section `## Output`", "\n".join(self.issues_for_skill(files)))

    def test_rejects_duplicate_substantive_instruction(self) -> None:
        files = self.valid_skill_files()
        duplicate = (
            "- Keep this long reusable instruction in exactly one owner and link every other skill to that owner instead.\n"
        )
        files[".agents/skills/axis-example/SKILL.md"] += duplicate
        files[".agents/skills/axis-other/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", "axis-other").replace("Axis Example", "Axis Other")
        files[".agents/skills/README.md"] += (
            "| Other | [axis-other/SKILL.md](./axis-other/SKILL.md) |\n"
        )

        self.assertIn("duplicate substantive instruction", "\n".join(self.issues_for_skill(files)))

    def test_skill_reference_target_resolves_parent_reference(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            skills_root = root / ".agents" / "skills"
            skill_dir = skills_root / "axis-example"
            skill_dir.mkdir(parents=True)
            (skills_root / "reference.md").write_text("# Reference\n", encoding="utf-8")
            skill_md = skill_dir / "SKILL.md"
            skill_md.write_text("# Example\n\nSee [reference.md](../reference.md).\n", encoding="utf-8")

            issues = axis.repo_skill_reference_issues(skill_md, skill_md.read_text(encoding="utf-8"), root=root)

        self.assertEqual([], issues)

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

    def test_rejects_extra_frontmatter_fields(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("description:", "metadata: extra\ndescription:")

        self.assertIn("frontmatter supports only", "\n".join(self.issues_for_skill(files)))

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

    def test_rejects_raw_repo_workflow_commands_in_skill_instructions(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/axis-example/SKILL.md"] += (
            "\n"
            "```bash\n"
            "npm run ci\n"
            "dotnet test\n"
            "python docs/scripts/render-visuals.py\n"
            "```\n"
            "Inline command: `npm run test`.\n"
        )

        issues = self.issues_for_skill(files)

        joined = "\n".join(issues)
        self.assertIn("raw skill workflow command `npm run ci`", joined)
        self.assertIn("use `python scripts/axis.py frontend ...`", joined)
        self.assertIn("raw skill workflow command `dotnet test`", joined)
        self.assertIn("use `python scripts/axis.py dotnet ...`", joined)
        self.assertIn("raw skill workflow command `python docs/scripts/render-visuals.py`", joined)
        self.assertIn("use an approved project wrapper", joined)
        self.assertIn("raw skill workflow command `npm run test`", joined)

    def test_current_repository_skills_still_pass(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_repo_skills())


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
    def test_iter_module_names_respects_gitignore(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            subprocess.run(
                ["git", "init"],
                cwd=root,
                check=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            (root / ".gitignore").write_text(
                "bin/\n"
                "obj/\n"
                "node_modules/\n"
                "src/Modules/IgnoredModule/\n",
                encoding="utf-8",
            )
            modules_dir = root / "src" / "Modules"
            generated_only = modules_dir / "GeneratedOnly"
            (generated_only / "Axis.GeneratedOnly.Domain" / "obj").mkdir(parents=True)
            (generated_only / "Axis.GeneratedOnly.Domain" / "obj" / "project.assets.json").write_text(
                "{}",
                encoding="utf-8",
            )
            dependency_dir = modules_dir / "node_modules"
            (dependency_dir / "package").mkdir(parents=True)
            (dependency_dir / "package" / "Generated.cs").write_text(
                "public sealed class Generated {}",
                encoding="utf-8",
            )
            ignored_module = modules_dir / "IgnoredModule"
            (ignored_module / "Axis.IgnoredModule.Domain").mkdir(parents=True)
            (ignored_module / "Axis.IgnoredModule.Domain" / "Ignored.cs").write_text(
                "public sealed class Ignored {}",
                encoding="utf-8",
            )
            real_module = modules_dir / "Identity"
            (real_module / "Axis.Identity.Domain").mkdir(parents=True)
            (real_module / "Axis.Identity.Domain" / "Axis.Identity.Domain.csproj").write_text(
                "<Project />",
                encoding="utf-8",
            )

            original_root = axis_repo.ROOT
            original_modules_dir = axis_repo.MODULES_DIR
            axis_repo.ROOT = root
            axis_repo.MODULES_DIR = modules_dir
            try:
                self.assertEqual(["Identity"], axis_repo.iter_module_names())
            finally:
                axis_repo.ROOT = original_root
                axis_repo.MODULES_DIR = original_modules_dir

    def test_iter_module_names_fallback_skips_dependency_root(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            modules_dir = Path(temp) / "src" / "Modules"
            dependency_dir = modules_dir / "node_modules" / "package"
            dependency_dir.mkdir(parents=True)
            (dependency_dir / "Generated.cs").write_text(
                "public sealed class Generated {}",
                encoding="utf-8",
            )
            real_module = modules_dir / "Identity" / "Axis.Identity.Domain"
            real_module.mkdir(parents=True)
            (real_module / "Axis.Identity.Domain.csproj").write_text(
                "<Project />",
                encoding="utf-8",
            )

            original_modules_dir = axis_repo.MODULES_DIR
            axis_repo.MODULES_DIR = modules_dir
            try:
                with mock.patch.object(axis_repo, "git_visible_paths_under", return_value=None):
                    self.assertEqual(["Identity"], axis_repo.iter_module_names())
            finally:
                axis_repo.MODULES_DIR = original_modules_dir

    def test_module_code_change_alone_does_not_force_doc_activity(self) -> None:
        self.assertEqual(
            [],
            doc_drift_domains.check_readme_api_status(["src/Modules/Identity/Axis.Identity.Domain/User.cs"]),
        )


if __name__ == "__main__":
    unittest.main()
