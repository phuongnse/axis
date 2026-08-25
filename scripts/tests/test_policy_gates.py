from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from datetime import date, timedelta
from unittest import mock
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

import acceptance_evidence  # noqa: E402
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


class TestAcceptanceEvidenceCommands(unittest.TestCase):
    def test_browser_e2e_command_accepts_global_compose_overlays(self) -> None:
        commands = (
            "python scripts/axis.py local-dev e2e -- e2e/sample.pw.ts",
            "python scripts/axis.py local-dev --compose-overlay product.yml e2e -- e2e/sample.pw.ts",
            "python scripts/axis.py local-dev --compose-overlay first.yml --compose-overlay second.yaml e2e",
        )

        for command in commands:
            with self.subTest(command=command):
                self.assertTrue(acceptance_evidence.is_browser_e2e_command(command))

    def test_browser_e2e_command_rejects_invalid_global_arguments(self) -> None:
        commands = (
            "python scripts/axis.py local-dev --compose-overlay",
            "python scripts/axis.py local-dev --compose-overlay e2e",
            "python scripts/axis.py local-dev --compose-overlay --unknown e2e",
            "python scripts/axis.py local-dev --unknown value e2e",
            "python scripts/axis.py local-dev status -- e2e",
            'python scripts/axis.py frontend test sample.test.tsx -t "local-dev e2e"',
        )

        for command in commands:
            with self.subTest(command=command):
                self.assertFalse(acceptance_evidence.is_browser_e2e_command(command))


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

    def test_use_case_inventory_layout_rejects_hub_without_a_direct_spec(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            domain = use_cases / "supporting-domain"
            domain.mkdir(parents=True)
            (use_cases / "README.md").write_text("# Use Cases\n", encoding="utf-8")
            (domain / "README.md").write_text("# Supporting Domain\n", encoding="utf-8")

            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.check_use_case_inventory_layout()
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        self.assertIn("domain hub must own at least one direct use-case spec", "\n".join(issues))

    def test_use_case_inventories_require_exact_links_and_derived_status(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            domain = use_cases / "example"
            domain.mkdir(parents=True)
            (use_cases / "README.md").write_text(
                """## Current Use Cases

| Domain | Use case | Status |
|---|---|---|
| [Example](./example/README.md) | [Wrong](./example/wrong.md) | Not started |
""",
                encoding="utf-8",
            )
            (domain / "README.md").write_text(
                """## Current Use Cases

| Use case | Status |
|---|---|
| [Sample](./sample.md) | Not started |
""",
                encoding="utf-8",
            )
            sample = domain / "sample.md"
            sample.write_text(
                """> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Application | Done |
""",
                encoding="utf-8",
            )
            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.validate_use_case_inventories([sample])
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        joined = "\n".join(issues)
        self.assertIn("status for `./sample.md` must be `Done`", joined)
        self.assertIn("is missing `./example/sample.md`", joined)
        self.assertIn("references non-spec `./example/wrong.md`", joined)

    def test_supporting_domain_status_is_derived_from_owning_use_case_layer(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            owner_dir = use_cases / "identity-governance"
            support_dir = use_cases / "audit"
            owner_dir.mkdir(parents=True)
            support_dir.mkdir(parents=True)
            (use_cases / "README.md").write_text(
                """## Supporting Domains

| Domain | Layer | Responsibilities |
|---|---|---|
| [Audit](./audit/README.md) | Audit | Audit projection. |
""",
                encoding="utf-8",
            )
            owner = owner_dir / "create-workspace.md"
            owner.write_text(
                """> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Audit | Partial |
""",
                encoding="utf-8",
            )
            (support_dir / "README.md").write_text(
                """## Supporting Responsibilities

| Owning use case | Layer | Responsibility | Status |
|---|---|---|---|
| [Create](../identity-governance/create-workspace.md) | Audit | Project creation outcome. | Not started |
""",
                encoding="utf-8",
            )

            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.validate_supporting_domain_inventories([owner])
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        self.assertIn(
            "status for `../identity-governance/create-workspace.md` layer `Audit` must be `Partial`, found `Not started`",
            "\n".join(issues),
        )

    def test_supporting_domain_requires_every_owner_of_its_declared_layer(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            use_cases = root / "docs" / "use-cases"
            owner_dir = use_cases / "identity-governance"
            support_dir = use_cases / "audit"
            owner_dir.mkdir(parents=True)
            support_dir.mkdir(parents=True)
            (use_cases / "README.md").write_text(
                """## Supporting Domains

| Domain | Layer | Responsibilities |
|---|---|---|
| [Audit](./audit/README.md) | Audit | Audit projection. |
""",
                encoding="utf-8",
            )
            owner_template = """> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Audit | Partial |
"""
            first_owner = owner_dir / "create-workspace.md"
            second_owner = owner_dir / "switch-workspace.md"
            first_owner.write_text(owner_template, encoding="utf-8")
            second_owner.write_text(owner_template, encoding="utf-8")
            (support_dir / "README.md").write_text(
                """## Supporting Responsibilities

| Owning use case | Layer | Responsibility | Status |
|---|---|---|---|
| [Create](../identity-governance/create-workspace.md) | Audit | Project creation outcome. | Partial |
""",
                encoding="utf-8",
            )

            original_root = check_use_case_docs.ROOT
            original_use_cases = check_use_case_docs.USE_CASES
            check_use_case_docs.ROOT = root
            check_use_case_docs.USE_CASES = use_cases
            try:
                issues = check_use_case_docs.validate_supporting_domain_inventories([first_owner, second_owner])
            finally:
                check_use_case_docs.ROOT = original_root
                check_use_case_docs.USE_CASES = original_use_cases

        self.assertIn(
            "missing supporting responsibility for `../identity-governance/switch-workspace.md` layer `Audit`",
            "\n".join(issues),
        )

    def issues_for_use_case(self, callout: str, ac_line: str = "- **AC-001** Works.") -> list[str]:
        if "## Acceptance Test Matrix" in callout:
            matrix, status = callout.split("> **Implementation status**", maxsplit=1)
            callout = "> **Implementation status**" + status
        else:
            matrix = """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |

"""
        return self.issues_for_document(
            f"""# Sample use case

## Purpose

Ship user value.

## Primary actor

- User

## Preconditions

- User can start the flow.

## Trigger

- User starts the flow.

## Success guarantee

- The requested outcome is durable.

## Minimal guarantee

- Failure leaves the current state safe.

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

    def test_rejects_missing_use_case_guarantee_sections(self) -> None:
        content = """# Sample use case

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

- **AC-001** Works.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |

## Out Of Scope

- N/A.
"""
        issues = self.issues_for_document(content)

        joined = "\n".join(issues)
        self.assertIn("missing preconditions section", joined)
        self.assertIn("missing success guarantee section", joined)
        self.assertIn("missing minimal guarantee section", joined)

    def test_rejects_use_case_sections_out_of_order(self) -> None:
        content = """# Sample use case

## Purpose

Ship user value.

## Preconditions

- User can start.

## Primary actor

- User

## Trigger

- User starts.

## Success guarantee

- Outcome is durable.

## Minimal guarantee

- Failure is safe.

## Main flow

1. User completes the goal.

## Alternate / error flows

- Failure stays safe.

## Acceptance Criteria

- **AC-001** Works.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |

## Out Of Scope

- N/A.
"""
        issues = self.issues_for_document(content)

        self.assertIn("canonical use-case order", "\n".join(issues))

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

    def test_rejects_partial_status_without_structured_gap_rows(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | Partial |
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

        self.assertIn("Gaps vs spec must contain a markdown table", "\n".join(issues))

    def test_accepts_partial_status_with_structured_gap_rows(self) -> None:
        issues = self.issues_for_use_case(
            """> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | Partial |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Runtime evidence remains pending. |
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

## Preconditions

- User can start the flow.

## Trigger

- User starts the flow.

## Success guarantee

- The requested outcome is durable.

## Minimal guarantee

- Failure leaves the current state safe.

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
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend test tests/sample.test.tsx` |

""",
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend test tests/sample.test.tsx` |
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
| AT-001 | `frontend/tests/sample.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/SampleTests.cs` | `python scripts/axis.py frontend test tests/sample.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
""",
            evidence_files=(
                "frontend/tests/sample.test.tsx",
                "tests/Api/Axis.Api.Tests/Identity/SampleTests.cs",
            ),
        )

        self.assertEqual([], issues)

    def test_accepts_mcp_boundary_with_contract_evidence(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | API/MCP boundaries | API and typed MCP stay aligned | AC-001 | API integration test + MCP contract test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Api/Axis.Api.Tests/Identity/SampleTests.cs`, `tests/Tools/Axis.Mcp.Tests/McpApiCoverageTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`, `python scripts/axis.py check mcp-api-coverage`, `python scripts/axis.py check mcp-contracts`, `python scripts/axis.py check mcp-tool-safety` |
""",
            evidence_files=(
                "tests/Api/Axis.Api.Tests/Identity/SampleTests.cs",
                "tests/Tools/Axis.Mcp.Tests/McpApiCoverageTests.cs",
            ),
        )

        self.assertEqual([], issues)

    def test_mcp_contract_evidence_requires_all_mcp_gates(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | API/MCP boundaries | API and typed MCP stay aligned | AC-001 | MCP contract test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `tests/Tools/Axis.Mcp.Tests/McpApiCoverageTests.cs` | `python scripts/axis.py check mcp-api-coverage` |
""",
            evidence_files=("tests/Tools/Axis.Mcp.Tests/McpApiCoverageTests.cs",),
        )

        joined = "\n".join(issues)
        self.assertIn("python scripts/axis.py check mcp-contracts", joined)
        self.assertIn("python scripts/axis.py check mcp-tool-safety", joined)

    def test_accepts_immutable_external_browser_evidence(self) -> None:
        checkpoint = "6eb817c02fda580fc9afee0c37b2b7e0a8c4735c"
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |"""
            ),
            evidence_doc=f"""# Sample Evidence

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@{checkpoint}/tests/product.pw.ts` | `external://axis-reference-product@{checkpoint}/npm run test:e2e` |
""",
        )

        self.assertEqual([], issues)

    def test_rejects_floating_or_mixed_external_evidence_provenance(self) -> None:
        checkpoint = "6eb817c02fda580fc9afee0c37b2b7e0a8c4735c"
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |"""
            ),
            evidence_doc=f"""# Sample Evidence

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@main/tests/product.pw.ts`, `frontend/e2e/sample.pw.ts` | `external://axis-reference-product@{checkpoint}/npm run test:e2e` |
""",
            evidence_files=("frontend/e2e/sample.pw.ts",),
        )

        joined = "\n".join(issues)
        self.assertIn("must use `external://<repository>@<40-character-commit>/<path>`", joined)
        self.assertIn("must not mix local and external provenance", joined)

    def test_rejects_external_command_bound_to_a_different_checkpoint(self) -> None:
        path_checkpoint = "6eb817c02fda580fc9afee0c37b2b7e0a8c4735c"
        command_checkpoint = "1111111111111111111111111111111111111111"
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |"""
            ),
            evidence_doc=f"""# Sample Evidence

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@{path_checkpoint}/tests/product.pw.ts` | `external://axis-reference-product@{command_checkpoint}/npm run test:e2e` |
""",
        )

        self.assertIn("must bind one repository and commit", "\n".join(issues))

    def test_external_browser_evidence_keeps_browser_command_requirement(self) -> None:
        checkpoint = "6eb817c02fda580fc9afee0c37b2b7e0a8c4735c"
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User completes flow | AC-001 | Browser automation | Yes |"""
            ),
            evidence_doc=f"""# Sample Evidence

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@{checkpoint}/tests/product.pw.ts` | `external://axis-reference-product@{checkpoint}/npm run test:unit` |
""",
        )

        self.assertIn("Browser automation Commands must run the external browser command", "\n".join(issues))

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
| AT-001, AT-002 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py frontend test tests/sample.test.tsx` |
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
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py local-dev e2e -- e2e/sample.pw.ts` |
""",
            evidence_files=("frontend/tests/sample.test.tsx",),
        )

        self.assertIn("Browser automation must reference a committed `frontend/e2e/*.pw.ts` test", "\n".join(issues))

    def test_browser_use_case_evidence_requires_canonical_local_dev_runner(self) -> None:
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
| AT-001 | `frontend/e2e/sample.pw.ts` | `python scripts/axis.py frontend test tests/sample.test.tsx` |
""",
            evidence_files=("frontend/e2e/sample.pw.ts",),
        )

        self.assertIn(
            "Browser automation Commands must run Playwright through scripts/axis.py",
            "\n".join(issues),
        )

    def test_browser_use_case_evidence_rejects_runner_name_in_arguments(self) -> None:
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
| AT-001 | `frontend/e2e/sample.pw.ts` | `python scripts/axis.py frontend test tests/sample.test.tsx -t "local-dev e2e"` |
""",
            evidence_files=("frontend/e2e/sample.pw.ts",),
        )

        self.assertIn(
            "Browser automation Commands must run Playwright through scripts/axis.py",
            "\n".join(issues),
        )

    def test_ui_component_evidence_rejects_e2e_script_variant(self) -> None:
        issues = self.issues_for_document(
            self.complete_use_case_document(
                """| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | User completes flow | AC-001 | UI component test | Yes |"""
            ),
            evidence_doc="""# Sample Evidence

> **Navigation**: [docs/use-cases/example/sample.md](./sample.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/tests/sample.test.tsx` | `python scripts/axis.py local-dev e2e -- e2e/sample.pw.ts` |
""",
            evidence_files=("frontend/tests/sample.test.tsx",),
        )

        self.assertIn(
            "UI component test Commands must run frontend tests through scripts/axis.py",
            "\n".join(issues),
        )

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

    def test_accepts_cross_layer_and_architecture_boundaries(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | API/Infrastructure boundaries | Runtime and persistence cooperate | AC-001 | API integration test | Yes |
| AT-002 | Architecture boundary | Public dependency direction is preserved | AC-001 | Architecture test | No |

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

        self.assertNotIn("invalid Boundary", "\n".join(issues))

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
    def test_assessment_artifacts_are_not_foundation_specs(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            foundation_root = root / "docs" / "foundations"
            surface = foundation_root / "app-shell"
            surface.mkdir(parents=True)
            (surface / "app-frame.md").write_text("# App Frame\n", encoding="utf-8")
            (surface / "app-frame.assessment.md").write_text(
                "# App Frame Assessment\n", encoding="utf-8"
            )

            original_root = check_foundation_docs.ROOT
            original_foundations = check_foundation_docs.FOUNDATIONS
            check_foundation_docs.ROOT = root
            check_foundation_docs.FOUNDATIONS = foundation_root
            try:
                self.assertEqual([surface / "app-frame.md"], check_foundation_docs.iter_foundation_files())
            finally:
                check_foundation_docs.ROOT = original_root
                check_foundation_docs.FOUNDATIONS = original_foundations

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

## Consumers

- Authenticated routes

## Activation

- An authenticated route renders.

## Guarantees

- The frame renders the route safely.

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
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend test src/components/shared/AppShell.test.tsx` |
| AT-002 | `frontend/e2e/app-frame.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
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
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend test src/components/shared/AppShell.test.tsx` |
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
| AT-001 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py frontend test src/components/shared/AppShell.test.tsx` |
| AT-002 | `frontend/src/components/shared/AppShell.test.tsx` | `python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts` |
""",
            evidence_files=("frontend/src/components/shared/AppShell.test.tsx",),
        )

        self.assertIn("Browser automation must reference a committed `frontend/e2e/*.pw.ts` test", "\n".join(issues))

    def test_browser_foundation_evidence_requires_canonical_local_dev_runner(self) -> None:
        wrong_runner = self.valid_evidence_doc().replace(
            "python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts",
            "python scripts/axis.py frontend test tests/app-shell.test.tsx",
        )
        issues = self.issues_for_foundation(
            evidence_doc=wrong_runner,
            evidence_files=(
                "frontend/src/components/shared/AppShell.test.tsx",
                "frontend/e2e/app-frame.pw.ts",
            ),
        )

        self.assertIn(
            "Browser automation Commands must run Playwright through scripts/axis.py",
            "\n".join(issues),
        )

    def test_browser_foundation_evidence_rejects_runner_name_in_arguments(self) -> None:
        near_miss = self.valid_evidence_doc().replace(
            "python scripts/axis.py local-dev e2e -- e2e/app-frame.pw.ts",
            'python scripts/axis.py frontend test tests/app-shell.test.tsx -t "local-dev e2e"',
        )
        issues = self.issues_for_foundation(
            evidence_doc=near_miss,
            evidence_files=(
                "frontend/src/components/shared/AppShell.test.tsx",
                "frontend/e2e/app-frame.pw.ts",
            ),
        )

        self.assertIn(
            "Browser automation Commands must run Playwright through scripts/axis.py",
            "\n".join(issues),
        )

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

    def test_rejects_legacy_foundation_flow_sections(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "foundations" / "app-shell" / "app-frame.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# App Frame

## Purpose

Provide an app frame.

## Primary actor

- User

## Trigger

- User opens a route.

## Main flow

1. System renders the frame.

## Alternate / error flows

- None.

## Acceptance Criteria

- **AC-001** Frame renders.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Frame renders. | AC-001 | UI component test | Yes |

## Out Of Scope

- N/A.
""",
                encoding="utf-8",
            )
            original_root = check_foundation_docs.ROOT
            check_foundation_docs.ROOT = root
            try:
                issues = check_foundation_docs.validate_sections(check_foundation_docs.foundation_document(path))
            finally:
                check_foundation_docs.ROOT = original_root

        joined = "\n".join(issues)
        self.assertIn("missing consumers section", joined)
        self.assertIn("missing activation section", joined)
        self.assertIn("missing guarantees section", joined)

    def test_rejects_foundation_sections_out_of_order(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "foundations" / "app-shell" / "app-frame.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                """# App Frame

## Purpose

Provide an app frame.

## Activation

- An authenticated route renders.

## Consumers

- Authenticated routes

## Guarantees

- The frame renders safely.

## Alternate / error flows

- None.

## Acceptance Criteria

- **AC-001** Frame renders.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | UI component | Frame renders. | AC-001 | UI component test | Yes |

## Out Of Scope

- N/A.
""",
                encoding="utf-8",
            )
            original_root = check_foundation_docs.ROOT
            check_foundation_docs.ROOT = root
            try:
                issues = check_foundation_docs.validate_sections(check_foundation_docs.foundation_document(path))
            finally:
                check_foundation_docs.ROOT = original_root

        self.assertIn("canonical foundation order", "\n".join(issues))

    def test_foundation_inventories_require_exact_links_and_derived_status(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            foundations = root / "docs" / "foundations"
            surface = foundations / "app-shell"
            surface.mkdir(parents=True)
            (foundations / "README.md").write_text(
                """## Current Foundations

| Surface | Foundation | Status |
|---|---|---|
| [App Shell](./app-shell/README.md) | [Wrong](./app-shell/wrong.md) | Not started |
""",
                encoding="utf-8",
            )
            (surface / "README.md").write_text(
                """## Foundations

| Foundation | Status |
|---|---|
| [App Frame](./app-frame.md) | Not started |
""",
                encoding="utf-8",
            )
            app_frame = surface / "app-frame.md"
            app_frame.write_text(
                """> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Frontend | Done |
""",
                encoding="utf-8",
            )
            original_root = check_foundation_docs.ROOT
            original_foundations = check_foundation_docs.FOUNDATIONS
            check_foundation_docs.ROOT = root
            check_foundation_docs.FOUNDATIONS = foundations
            try:
                issues = check_foundation_docs.validate_foundation_inventories([app_frame])
            finally:
                check_foundation_docs.ROOT = original_root
                check_foundation_docs.FOUNDATIONS = original_foundations

        joined = "\n".join(issues)
        self.assertIn("status for `./app-frame.md` must be `Done`", joined)
        self.assertIn("is missing `./app-shell/app-frame.md`", joined)
        self.assertIn("references non-spec `./app-shell/wrong.md`", joined)

    def test_foundation_status_details_are_strict_only_for_changed_paths(self) -> None:
        content = """> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Frontend | Done |
"""
        doc = check_foundation_docs.FoundationDocument(
            path=ROOT / "docs" / "foundations" / "app-shell" / "sample.md",
            text=content,
            sections={},
            h2_headings=[],
        )

        self.assertEqual([], check_foundation_docs.validate_implementation_status(doc, strict_status=False))
        self.assertIn(
            "missing implementation status gaps vs spec section",
            "\n".join(check_foundation_docs.validate_implementation_status(doc, strict_status=True)),
        )


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

    def test_rejects_fully_qualified_axis_type_inside_implementation(self) -> None:
        issues = self.issue_text(
            [
                (
                    "src/Modules/Rules/Axis.Rules.Domain/Example.cs",
                    "return Axis.Shared.Domain.Primitives.Result.Failure<Rule>(error);",
                )
            ]
        )
        self.assertIn("Fully-qualified Axis type introduced in implementation code", issues)

    def test_accepts_axis_type_alias_in_using_directive(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "src/Modules/Rules/Axis.Rules.Domain/Example.cs",
                    "using ResultFactory = Axis.Shared.Domain.Primitives.Result;",
                )
            ]
        )
        self.assertEqual([], issues)

    def test_accepts_axis_assembly_metadata_string(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "src/Modules/Rules/Axis.Rules.Domain/AssemblyInfo.cs",
                    '[assembly: InternalsVisibleTo("Axis.Rules.Infrastructure")]',
                )
            ]
        )
        self.assertEqual([], issues)

    def test_still_rejects_axis_type_in_assembly_attribute(self) -> None:
        issues = self.issue_text(
            [
                (
                    "src/Modules/Rules/Axis.Rules.Domain/AssemblyInfo.cs",
                    "[assembly: Example(typeof(Axis.Rules.Domain.RuleAssetDefinition))]",
                )
            ]
        )
        self.assertIn("Fully-qualified Axis type introduced in implementation code", issues)

    def test_ignores_todo_in_docs(self) -> None:
        issues = axis.doc_drift_added_line_issues([("docs/example.md", "TODO in docs is not this gate")])
        self.assertEqual([], issues)

    def test_rejects_placeholder_marker_in_frontend_source(self) -> None:
        issues = self.issue_text([("frontend/src/features/example/Example.tsx", "const value = 'placeholder';")])
        self.assertIn("New TODO/FIXME/stub marker introduced", issues)

    def test_rejects_double_quoted_placeholder_marker_in_frontend_source(self) -> None:
        issues = self.issue_text([("frontend/src/features/example/Example.tsx", 'const value = "placeholder";')])
        self.assertIn("New TODO/FIXME/stub marker introduced", issues)

    def test_accepts_placeholder_identifier_in_source(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "src/Modules/Rules/Axis.Rules.Domain/RuleAssetSource.cs",
                    "found.Add(placeholder);",
                )
            ]
        )
        self.assertEqual([], issues)

    def test_accepts_placeholder_user_copy_in_frontend_source(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "frontend/src/features/preferences/translations.ts",
                    "'Bind each {placeholder} to one configuration parameter.',",
                )
            ]
        )
        self.assertEqual([], issues)

    def test_accepts_placeholder_translation_key_in_frontend_source(self) -> None:
        issues = axis.doc_drift_added_line_issues(
            [
                (
                    "frontend/src/features/rules/components/RuleDraftEditor.tsx",
                    "label={t('rules.placeholder')}",
                )
            ]
        )
        self.assertEqual([], issues)

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
            return "\n".join(axis.documented_command_issues(files.keys(), root=root))

    def test_rejects_raw_repo_commands_in_documented_workflows(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": "\n".join(
                    [
                        "# Example",
                        "",
                        "```bash",
                        "dotnet build",
                        "dotnet ef migrations add AddWidget",
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
        self.assertIn(
            "use `python scripts/axis.py migration add <module> <Name>`",
            issues,
        )
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

    def test_rejects_documented_axis_commands_outside_the_cli_contract(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py frontend unsupported tests/sample.test.tsx`"
                ),
            }
        )

        self.assertIn("does not match the CLI contract", issues)

    def test_accepts_documented_axis_command_templates_without_executing_them(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py git sync --branch <branch>`"
                ),
            }
        )

        self.assertEqual("", issues)

    def test_rejects_unknown_routes_even_in_documented_command_templates(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py frontend unsupported [test-paths]`"
                ),
            }
        )

        self.assertIn("unknown command route `unsupported`", issues)

    def test_rejects_unknown_options_in_documented_command_templates(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py verify --unsupported <profile>`"
                ),
            }
        )

        self.assertIn("unknown option `--unsupported`", issues)

    def test_rejects_invalid_documented_commands_before_shell_redirection(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py verify --unsupported > doctor.txt`"
                ),
            }
        )

        self.assertIn("unrecognized arguments: --unsupported", issues)

    def test_redirection_target_placeholder_does_not_hide_invalid_command(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py verify --unsupported > <doctor-output>`"
                ),
            }
        )

        self.assertIn("unrecognized arguments: --unsupported", issues)

    def test_rejects_noncanonical_python_launchers_in_docs(self) -> None:
        issues = self.documented_issue_text(
            {
                "README.md": "\n".join(
                    [
                        "```bash",
                        "python3 scripts/axis.py verify",
                        "```",
                        "```powershell",
                        "py -3 scripts/axis.py verify",
                        "```",
                    ]
                ),
            }
        )

        self.assertEqual(2, issues.count("use `python scripts/axis.py ...`"))

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

    def test_added_lines_use_final_working_tree_diff_and_untracked_content(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            untracked = root / "docs" / "untracked.md"
            untracked.parent.mkdir(parents=True)
            untracked.write_text("untracked line\n", encoding="utf-8")

            outputs = {
                ("merge-base", "base", "HEAD"): "shared-base\n",
                ("diff", "--unified=0", "shared-base"): (
                    "+++ b/docs/committed.md\n"
                    "++++heading\n"
                    "+corrected committed line\n"
                    "+++ b/docs/staged.md\n"
                    "+staged line\n"
                    "+++ b/docs/unstaged.md\n"
                    "+unstaged line\n"
                ),
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
                ("docs/committed.md", "corrected committed line"),
                ("docs/staged.md", "staged line"),
                ("docs/unstaged.md", "unstaged line"),
                ("docs/untracked.md", "untracked line"),
            ],
            rows,
        )


class TestRenovateConfigGate(unittest.TestCase):
    def test_process_updates_are_reserved_for_the_lifecycle_host(self) -> None:
        config = json.loads(
            (axis.ROOT / ".github" / "renovate.json5").read_text(encoding="utf-8")
        )

        self.assertTrue(config["enabled"])
        self.assertFalse(config["automerge"])
        self.assertTrue(config["draftPR"])
        self.assertIn("pip-compile", config["enabledManagers"])
        self.assertFalse(config["pip_requirements"]["enabled"])
        self.assertEqual(
            ["/^requirements\\/process\\.txt$/"],
            config["pip-compile"]["managerFilePatterns"],
        )
        self.assertNotIn("postUpgradeTasks", config)
        rule = next(
            item
            for item in config["packageRules"]
            if "engineering-process" in item.get("matchPackageNames", [])
        )
        self.assertFalse(rule["enabled"])
        self.assertFalse(rule["automerge"])
        self.assertEqual(["at any time"], rule["schedule"])
        self.assertEqual(100, rule["prPriority"])
        self.assertEqual(
            [
                ".github/workflows/build-and-test.yml",
                ".github/workflows/dependency-security.yml",
                "requirements/process.in",
                "requirements/process.txt",
            ],
            rule["matchFileNames"],
        )
        self.assertEqual(
            ["engineering-process", "phuongnse/engineering-process"],
            rule["matchPackageNames"],
        )

        process_input = (axis.ROOT / "requirements" / "process.in").read_text(
            encoding="utf-8"
        )
        process_lock = (axis.ROOT / "requirements" / "process.txt").read_text(
            encoding="utf-8"
        )
        self.assertRegex(
            process_input,
            r"(?m)^engineering-process==[0-9]+\.[0-9]+\.[0-9]+$",
        )
        self.assertIn("pip-compile ", process_lock)
        self.assertIn("--generate-hashes", process_lock)
        workflow = (
            axis.ROOT / ".github" / "workflows" / "build-and-test.yml"
        ).read_text(encoding="utf-8")
        self.assertGreaterEqual(workflow.count("processctl adoption check"), 2)
        self.assertIn("automation/process/engineering-process", workflow)
        self.assertNotIn("automation/renovate/engineering-process", workflow)
        self.assertIn(
            "policy-verification.yml@2152dab51edd6c84163a71b48f50e6ad042eb331",
            workflow,
        )
        self.assertIn(
            "policy-verification:\n    name: policy-verification\n"
            "    permissions:\n      contents: read\n      pull-requests: read",
            workflow,
        )
        self.assertNotIn("independent-review.yml", workflow)

    def test_uses_project_frontend_runtime_for_validator(self) -> None:
        completed = axis.subprocess.CompletedProcess([], 0, stdout="", stderr="")
        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "run_frontend_npm", return_value=completed) as run_npm,
        ):
            self.assertEqual(0, axis.check_renovate_config())

        args = run_npm.call_args.args[0]
        self.assertEqual("exec", args[0])
        self.assertIn("renovate-config-validator", args)
        self.assertEqual(axis.ROOT, run_npm.call_args.kwargs["cwd"])


class TestVulnerablePackageGate(unittest.TestCase):
    @staticmethod
    def frontend_audit_report(*, severity: str = "moderate") -> dict[str, object]:
        return {
            "auditReportVersion": 2,
            "vulnerabilities": {
                "@hono/node-server": {
                    "name": "@hono/node-server",
                    "severity": severity,
                    "isDirect": False,
                    "via": [
                        {
                            "source": 1124006,
                            "name": "@hono/node-server",
                            "dependency": "@hono/node-server",
                            "title": "Path traversal",
                            "url": "https://github.com/advisories/GHSA-frvp-7c67-39w9",
                            "severity": severity,
                            "range": "<2.0.5",
                        }
                    ],
                    "effects": ["@modelcontextprotocol/sdk"],
                    "range": "<2.0.5",
                    "nodes": ["node_modules/@hono/node-server"],
                },
                "@modelcontextprotocol/sdk": {
                    "name": "@modelcontextprotocol/sdk",
                    "severity": severity,
                    "isDirect": False,
                    "via": ["@hono/node-server"],
                    "effects": ["shadcn"],
                    "range": ">=1.25.0",
                    "nodes": ["node_modules/@modelcontextprotocol/sdk"],
                },
                "shadcn": {
                    "name": "shadcn",
                    "severity": severity,
                    "isDirect": True,
                    "via": ["@modelcontextprotocol/sdk"],
                    "effects": [],
                    "range": ">=3.8.4",
                    "nodes": ["node_modules/shadcn"],
                },
            },
            "metadata": {
                "vulnerabilities": {
                    "info": 0,
                    "low": 0,
                    "moderate": 3 if severity == "moderate" else 0,
                    "high": 3 if severity == "high" else 0,
                    "critical": 0,
                    "total": 3,
                }
            },
        }

    @staticmethod
    def write_frontend_risk_acceptance(
        root: Path,
        *,
        accepted_on: str | None = None,
        expires_on: str | None = None,
    ) -> None:
        accepted_on = accepted_on or (date.today() - timedelta(days=15)).isoformat()
        expires_on = expires_on or (date.today() + timedelta(days=15)).isoformat()
        path = root / "frontend" / "dependency-risk-acceptances.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "acceptances": [
                        {
                            "advisory": "GHSA-frvp-7c67-39w9",
                            "severity": "moderate",
                            "dependencyPath": [
                                "shadcn",
                                "@modelcontextprotocol/sdk",
                                "@hono/node-server",
                            ],
                            "owner": "frontend-tooling",
                            "acceptedOn": accepted_on,
                            "expiresOn": expires_on,
                            "scope": "shadcn CLI tooling only; not imported by the SPA runtime",
                            "reason": "The compatible upstream dependency range has not reached the patched release.",
                            "remediation": "Remove this acceptance when shadcn permits @hono/node-server >=2.0.5.",
                        }
                    ],
                }
            )
            + "\n",
            encoding="utf-8",
        )

    def test_uses_absolute_solution_path_for_dotnet_list(self) -> None:
        calls: list[list[str]] = []

        def fake_run(args: list[str], **_kwargs):
            calls.append(args)
            return axis.subprocess.CompletedProcess(
                args,
                0,
                stdout=json.dumps({"version": 1, "projects": []}),
                stderr="",
            )

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
        self.assertEqual(["--format", "json", "--output-version", "1"], calls[0][-4:])

    def test_nuget_vulnerability_report_rejects_structured_findings(self) -> None:
        report = {
            "version": 1,
            "projects": [
                {
                    "path": "/repo/Example.csproj",
                    "frameworks": [
                        {
                            "framework": "net8.0",
                            "topLevelPackages": [
                                {
                                    "id": "Example.Package",
                                    "vulnerabilities": [
                                        {"severity": "High", "advisoryurl": "https://example.invalid/advisory"}
                                    ],
                                }
                            ],
                            "transitivePackages": [],
                        }
                    ]
                }
            ],
        }

        self.assertEqual(
            ["NuGet package 'Example.Package' has 1 known vulnerability record(s)"],
            axis.nuget_vulnerability_report_issues(report),
        )

    def test_nuget_vulnerability_report_rejects_invalid_schema(self) -> None:
        self.assertEqual(
            ["NuGet vulnerability report must use JSON output version 1"],
            axis.nuget_vulnerability_report_issues({"projects": []}),
        )

    def test_nuget_vulnerability_report_accepts_projects_without_findings(self) -> None:
        report = {
            "version": 1,
            "projects": [
                {"path": "/repo/One.csproj"},
                {"path": "/repo/Two.csproj", "frameworks": []},
            ],
        }

        self.assertEqual([], axis.nuget_vulnerability_report_issues(report))

    def test_nuget_vulnerability_report_requires_project_identity(self) -> None:
        report = {"version": 1, "projects": [{"frameworks": []}]}

        self.assertEqual(
            ["NuGet vulnerability report project 1 has invalid path"],
            axis.nuget_vulnerability_report_issues(report),
        )

    def test_nuget_vulnerability_report_rejects_framework_without_identity(self) -> None:
        report = {
            "version": 1,
            "projects": [{"path": "/repo/Example.csproj", "frameworks": [{}]}],
        }

        self.assertEqual(
            ["NuGet vulnerability report project 1 framework 1 has invalid identity"],
            axis.nuget_vulnerability_report_issues(report),
        )

    def test_nuget_vulnerability_report_rejects_package_without_identity(self) -> None:
        report = {
            "version": 1,
            "projects": [
                {
                    "path": "/repo/Example.csproj",
                    "frameworks": [
                        {
                            "framework": "net8.0",
                            "topLevelPackages": [{}],
                        }
                    ],
                }
            ],
        }

        self.assertEqual(
            ["NuGet vulnerability report topLevelPackages item 1 has invalid id"],
            axis.nuget_vulnerability_report_issues(report),
        )

    def test_nuget_vulnerability_report_rejects_package_without_findings_shape(self) -> None:
        report = {
            "version": 1,
            "projects": [
                {
                    "path": "/repo/Example.csproj",
                    "frameworks": [
                        {
                            "framework": "net8.0",
                            "topLevelPackages": [{"id": "Example.Package"}],
                        }
                    ],
                }
            ],
        }

        self.assertEqual(
            ["NuGet package 'Example.Package' has invalid vulnerabilities"],
            axis.nuget_vulnerability_report_issues(report),
        )

    def test_shadcn_cli_is_owned_as_a_development_dependency(self) -> None:
        package = json.loads((axis.ROOT / "frontend" / "package.json").read_text(encoding="utf-8"))

        self.assertNotIn("shadcn", package["dependencies"])
        self.assertIn("shadcn", package["devDependencies"])

    def test_frontend_dependency_versions_accept_exact_versions(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            frontend = root / "frontend"
            frontend.mkdir()
            node_version = axis.managed_tool_version("node")
            (frontend / ".nvmrc").write_text(f"{node_version}\n", encoding="utf-8")
            (frontend / "Dockerfile.dev").write_text(
                f"FROM node:{node_version}-alpine\n",
                encoding="utf-8",
            )
            (frontend / "package.json").write_text(
                json.dumps(
                    {
                        "packageManager": "npm@11.16.0",
                        "dependencies": {"stable": "1.2.3"},
                        "devDependencies": {"preview": "2.0.0-beta.1"},
                        "overrides": {"transitive": "3.4.5"},
                    }
                ),
                encoding="utf-8",
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.check_frontend_dependency_versions())

    def test_frontend_dependency_versions_reject_ranges_and_tags(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            frontend = root / "frontend"
            frontend.mkdir()
            node_version = axis.managed_tool_version("node")
            (frontend / ".nvmrc").write_text(f"{node_version}\n", encoding="utf-8")
            (frontend / "Dockerfile.dev").write_text(
                f"FROM node:{node_version}-alpine\n",
                encoding="utf-8",
            )
            (frontend / "package.json").write_text(
                json.dumps(
                    {
                        "packageManager": "npm@11.16.0",
                        "dependencies": {"ranged": "^1.2.3"},
                        "devDependencies": {"floating": "latest"},
                        "overrides": {},
                    }
                ),
                encoding="utf-8",
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_dependency_versions())

        output = stderr.getvalue()
        self.assertIn("dependencies.ranged", output)
        self.assertIn("devDependencies.floating", output)

    def test_frontend_gate_accepts_only_the_current_time_bounded_moderate_advisory(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(root)
            report = self.frontend_audit_report()
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 1, stdout=json.dumps(report), stderr=""),
                ) as run_npm,
                contextlib.redirect_stdout(io.StringIO()) as stdout,
            ):
                self.assertEqual(0, axis.check_frontend_vulnerable_packages())

        run_npm.assert_called_once_with(["audit", "--json"], capture=True)
        self.assertIn("GHSA-frvp-7c67-39w9", stdout.getvalue())

    def test_frontend_gate_uses_lock_graph_when_npm_omits_unaffected_direct_parent(self) -> None:
        report = self.frontend_audit_report()
        del report["vulnerabilities"]["shadcn"]
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(root)
            frontend = root / "frontend"
            (frontend / "package.json").write_text(
                json.dumps({"dependencies": {}, "devDependencies": {"shadcn": "4.13.1"}}),
                encoding="utf-8",
            )
            (frontend / "package-lock.json").write_text(
                json.dumps(
                    {
                        "packages": {
                            "": {"devDependencies": {"shadcn": "4.13.1"}},
                            "node_modules/shadcn": {
                                "dependencies": {"@modelcontextprotocol/sdk": "^1.26.0"}
                            },
                            "node_modules/@modelcontextprotocol/sdk": {
                                "dependencies": {"@hono/node-server": "^1.19.9"}
                            },
                        }
                    }
                ),
                encoding="utf-8",
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess(
                        [], 1, stdout=json.dumps(report), stderr=""
                    ),
                ),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.check_frontend_vulnerable_packages())

    def test_frontend_gate_rejects_high_advisory_even_when_moderate_acceptance_exists(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(root)
            report = self.frontend_audit_report(severity="high")
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 1, stdout=json.dumps(report), stderr=""),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("high vulnerabilities cannot be accepted", stderr.getvalue())
        self.assertIn("GHSA-frvp-7c67-39w9", stderr.getvalue())

    def test_frontend_gate_rejects_new_unaccepted_moderate_advisory(self) -> None:
        report = self.frontend_audit_report()
        root_advisory = report["vulnerabilities"]["@hono/node-server"]["via"][0]
        root_advisory["url"] = "https://github.com/advisories/GHSA-new1-new2-new3"
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(root)
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 1, stdout=json.dumps(report), stderr=""),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("GHSA-new1-new2-new3 is not accepted", stderr.getvalue())
        self.assertIn("GHSA-frvp-7c67-39w9 is stale", stderr.getvalue())

    def test_frontend_gate_rejects_vulnerability_without_a_resolvable_advisory(self) -> None:
        report = {
            "auditReportVersion": 2,
            "vulnerabilities": {
                "opaque-package": {
                    "name": "opaque-package",
                    "severity": "low",
                    "isDirect": True,
                    "via": [],
                    "effects": [],
                    "range": "*",
                    "nodes": ["node_modules/opaque-package"],
                }
            },
            "metadata": {},
        }
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "frontend").mkdir()
            (root / "frontend" / "dependency-risk-acceptances.json").write_text(
                '{"schemaVersion":1,"acceptances":[]}\n',
                encoding="utf-8",
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 1, stdout=json.dumps(report), stderr=""),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("opaque-package does not resolve to a GitHub advisory", stderr.getvalue())

    def test_frontend_gate_rejects_expired_acceptance(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(
                root,
                accepted_on="1999-12-03",
                expires_on="2000-01-01",
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess(
                        [], 1, stdout=json.dumps(self.frontend_audit_report()), stderr=""
                    ),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("expired on 2000-01-01", stderr.getvalue())

    def test_frontend_gate_rejects_acceptance_longer_than_thirty_days(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(
                root,
                accepted_on=(date.today() - timedelta(days=31)).isoformat(),
                expires_on=date.today().isoformat(),
            )
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess(
                        [], 1, stdout=json.dumps(self.frontend_audit_report()), stderr=""
                    ),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("exceeds the 30-day maximum", stderr.getvalue())

    def test_frontend_gate_rejects_stale_acceptance_after_advisory_disappears(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_frontend_risk_acceptance(root)
            report = {"auditReportVersion": 2, "vulnerabilities": {}, "metadata": {}}
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 0, stdout=json.dumps(report), stderr=""),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("GHSA-frvp-7c67-39w9 is stale", stderr.getvalue())

    def test_frontend_gate_rejects_invalid_audit_output(self) -> None:
        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                return_value=axis.subprocess.CompletedProcess([], 1, stdout="not-json", stderr="network failed"),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("valid npm audit JSON", stderr.getvalue())

    def test_frontend_gate_reports_structured_npm_audit_failure(self) -> None:
        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(
                axis,
                "run_frontend_npm",
                return_value=axis.subprocess.CompletedProcess(
                    [],
                    1,
                    stdout=json.dumps({"message": "registry unavailable", "error": {}}),
                    stderr="",
                ),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn(
            "npm audit did not return a vulnerability report: registry unavailable",
            stderr.getvalue(),
        )

    def test_frontend_gate_rejects_unexpected_npm_audit_exit_code(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "frontend").mkdir()
            (root / "frontend" / "dependency-risk-acceptances.json").write_text(
                '{"schemaVersion":1,"acceptances":[]}\n',
                encoding="utf-8",
            )
            report = {"auditReportVersion": 2, "vulnerabilities": {}, "metadata": {}}
            with (
                mock.patch.object(axis, "ROOT", root),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(
                    axis,
                    "run_frontend_npm",
                    return_value=axis.subprocess.CompletedProcess([], 2, stdout=json.dumps(report), stderr="failed"),
                ),
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.check_frontend_vulnerable_packages())

        self.assertIn("npm audit exited with 2", stderr.getvalue())


class TestToolVersionGates(unittest.TestCase):
    def test_run_replaces_inherited_invalid_temp_paths(self) -> None:
        completed = axis.subprocess.CompletedProcess(["tool"], 0)
        invalid = "/path/that/does/not/exist"
        with (
            mock.patch.dict(
                axis.os.environ,
                {"TMPDIR": invalid, "TEMP": invalid, "TMP": invalid},
            ),
            mock.patch.object(axis.tempfile, "gettempdir", return_value="/tmp"),
            mock.patch.object(axis.subprocess, "run", return_value=completed) as run,
        ):
            axis.run(["tool"], check=False)

        child_env = run.call_args.kwargs["env"]
        self.assertEqual("/tmp", child_env["TMPDIR"])
        self.assertEqual("/tmp", child_env["TEMP"])
        self.assertEqual("/tmp", child_env["TMP"])

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
        self.assertIn("expected 10.x", detail)

    def test_dotnet_sdk_rejects_portable_setup_major_drift_before_runtime_probe(self) -> None:
        with (
            mock.patch.object(axis, "managed_tool_version", return_value="9.0.100"),
            mock.patch.object(axis, "command_version_line") as command_version,
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn(".process/project.json pins 9.0.100", detail)
        command_version.assert_not_called()

    def test_dotnet_sdk_rejects_wrong_major(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(True, "9.0.100", "/usr/bin/dotnet"),
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("expected .NET SDK 10.x", detail)
        self.assertIn("docs/TECH_STACK.md", detail)
        self.assertIn("processctl setup", detail)

    def test_dotnet_sdk_missing_runtime_points_to_axis_managed_setup(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(False, "dotnet not found", "dotnet"),
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("processctl setup", detail)

    def test_frontend_toolchain_rejects_wrong_node_patch(self) -> None:
        with (
            mock.patch.object(axis, "required_node_version", return_value=(True, "24.18.0")),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(True, "v24.13.0", "/usr/bin/node"),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_frontend_toolchain())

        self.assertIn("expected Node 24.18.0", stderr.getvalue())
        self.assertIn("frontend/.nvmrc", stderr.getvalue())

    def test_missing_node_points_to_axis_managed_setup(self) -> None:
        with (
            mock.patch.object(axis, "required_node_version", return_value=(True, "24.18.0")),
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(False, "node not found", "node"),
            ),
        ):
            ok, detail = axis.node_version_status({})

        self.assertFalse(ok)
        self.assertIn("processctl setup", detail)

    def test_wrong_npm_points_to_axis_managed_setup(self) -> None:
        completed = axis.subprocess.CompletedProcess(
            ["node", "npm-cli.js", "--version"],
            0,
            stdout="11.15.0\n",
            stderr="",
        )
        with (
            mock.patch.object(axis, "npm_cli_command", return_value=["node", "npm-cli.js"]),
            mock.patch.object(axis, "run_optional", return_value=completed),
        ):
            ok, detail = axis.npm_version_status({})

        self.assertFalse(ok)
        self.assertIn("processctl setup", detail)

    def test_npm_cli_uses_native_node_and_script_without_batch_shim(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            node = Path(temp) / "node.exe"
            npm_cli = Path(temp) / "node_modules" / "npm" / "bin" / "npm-cli.js"
            node.write_bytes(b"")
            npm_cli.parent.mkdir(parents=True)
            npm_cli.write_text("", encoding="utf-8")

            with mock.patch.object(
                axis.shutil,
                "which",
                side_effect=lambda name, **_kwargs: str(node) if name == "node" else None,
            ):
                command = axis.npm_cli_command(env={"PATH": temp})

        self.assertEqual([str(node.resolve()), str(npm_cli.resolve())], command)
        self.assertNotIn(".cmd", " ".join(command).lower())
        self.assertNotIn(".bat", " ".join(command).lower())

    def test_missing_lychee_points_to_axis_managed_setup(self) -> None:
        with (
            mock.patch.object(axis, "find_lychee", return_value=None),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            rc = axis.check_markdown_links_for_paths([])

        self.assertEqual(1, rc)
        self.assertIn("processctl setup", stderr.getvalue())

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
            return axis.subprocess.CompletedProcess(command, 2, stdout="", stderr="/missing/chromium\n")

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
        ):
            ok, detail = axis.playwright_chromium_status({"PATH": "/tmp/node"})

        self.assertFalse(ok)
        self.assertIn("/missing/chromium", detail)
        self.assertIn("python scripts/axis.py frontend install-browsers", detail)

    def test_playwright_chromium_status_rejects_installed_browser_missing_native_library(self) -> None:
        def fake_run(command: list[str], **_kwargs):
            return axis.subprocess.CompletedProcess(
                command,
                1,
                stdout="/cache/chromium/chrome\n",
                stderr=(
                    "/cache/chromium/chrome: error while loading shared libraries: "
                    "libnspr4.so: cannot open shared object file\n"
                ),
            )

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
        ):
            ok, detail = axis.playwright_chromium_status({"PATH": "/tmp/node"})

        self.assertFalse(ok)
        self.assertIn("/cache/chromium/chrome is installed but cannot launch", detail)
        self.assertIn("missing native library `libnspr4.so`", detail)
        self.assertIn("python scripts/axis.py local-dev e2e", detail)
        self.assertIn("does not install with sudo or an OS package manager", detail)

    def test_playwright_chromium_status_requires_a_successful_launch(self) -> None:
        commands: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            commands.append(command)
            return axis.subprocess.CompletedProcess(
                command,
                0,
                stdout="/cache/chromium/chrome\n",
                stderr="",
            )

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "resolve_exe", side_effect=lambda name, **_kwargs: name),
        ):
            ok, detail = axis.playwright_chromium_status({"PATH": "/tmp/node"})

        self.assertTrue(ok)
        self.assertEqual("/cache/chromium/chrome", detail)
        self.assertIn("chromium.launch", commands[0][-1])
        self.assertIn("browser.close", commands[0][-1])

    def test_local_dev_certificate_state_covers_shared_runtime_readiness(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            marker = Path(temp) / "trusted-rootCA.sha1"
            with (
                mock.patch.object(
                    axis,
                    "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                    marker,
                ),
                mock.patch.object(
                    axis,
                    "local_dev_certificates_valid",
                    side_effect=[True, False, False],
                ),
            ):
                self.assertEqual(
                    axis.LocalDevCertificateState.OPENSSL_UNAVAILABLE,
                    axis.local_dev_certificate_state(None),
                )
                self.assertEqual(
                    axis.LocalDevCertificateState.READY,
                    axis.local_dev_certificate_state("/usr/bin/openssl"),
                )
                self.assertEqual(
                    axis.LocalDevCertificateState.MISSING_OR_INVALID,
                    axis.local_dev_certificate_state("/usr/bin/openssl"),
                )
                marker.write_text("managed\n", encoding="utf-8")
                self.assertEqual(
                    axis.LocalDevCertificateState.MANAGED_TRUST_MARKER_INVALID,
                    axis.local_dev_certificate_state("/usr/bin/openssl"),
                )

    def test_local_dev_certificate_remediation_is_derived_from_state(self) -> None:
        self.assertIn(
            "local-dev certs",
            axis.local_dev_certificate_remediation(
                axis.LocalDevCertificateState.MISSING_OR_INVALID
            ),
        )
        managed_detail = axis.local_dev_certificate_remediation(
            axis.LocalDevCertificateState.MANAGED_TRUST_MARKER_INVALID
        )
        untrust = "python scripts/axis.py local-dev untrust-certs"
        renew = "python scripts/axis.py local-dev certs --renew"
        trust = "python scripts/axis.py local-dev trust-certs"
        self.assertLess(managed_detail.index(untrust), managed_detail.index(renew))
        self.assertLess(managed_detail.index(renew), managed_detail.index(trust))

    def test_required_local_dev_certificates_fail_with_state_derived_guidance(self) -> None:
        with (
            mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
            mock.patch.object(
                axis,
                "local_dev_certificate_state",
                return_value=axis.LocalDevCertificateState.MANAGED_TRUST_MARKER_INVALID,
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.require_local_dev_certificates("local-dev e2e"))

        detail = stderr.getvalue()
        self.assertIn("local-dev e2e: local HTTPS certificate preflight failed", detail)
        self.assertIn("python scripts/axis.py local-dev untrust-certs", detail)
        self.assertIn("python scripts/axis.py local-dev certs --renew", detail)
        self.assertIn("python scripts/axis.py local-dev trust-certs", detail)

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

class TestVerifyGate(unittest.TestCase):
    def test_docker_readiness_requires_engine_and_compose(self) -> None:
        with (
            mock.patch.object(axis, "_docker_info_ok", return_value=True),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(0, axis.check_docker())

        self.assertIn("docker info and docker compose version work", stdout.getvalue())

    def test_docker_readiness_rejects_engine_without_compose(self) -> None:
        with (
            mock.patch.object(axis, "_docker_info_ok", return_value=True),
            mock.patch.object(axis, "_docker_compose_ok", return_value=False),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_docker())

        self.assertIn("Docker Compose v2 is unavailable", stderr.getvalue())

    def test_docker_readiness_rejects_compose_without_engine(self) -> None:
        with (
            mock.patch.object(axis, "_docker_info_ok", return_value=False),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.check_docker())

        self.assertIn("no reachable Docker endpoint detected", stderr.getvalue())

    def test_process_profiles_bind_browser_docker_and_setup_mutation_contracts(self) -> None:
        project = json.loads(axis.PROCESS_PROJECT_PATH.read_text(encoding="utf-8"))
        development_command = project["profiles"]["development"][0]["run"]
        self.assertEqual(["python", "scripts/axis.py", "verify"], development_command)
        self.assertIn("docker-engine", project["environment"]["profiles"]["development"])
        self.assertIn("docker-engine", project["environment"]["profiles"]["review"])
        docker_requirement = next(
            requirement
            for requirement in project["environment"]["requirements"]
            if requirement["id"] == "docker-engine"
        )
        self.assertIn("Docker Compose v2", docker_requirement["description"])
        setup = {
            action["id"]: action
            for action in project["environment"]["setupActions"]
        }
        self.assertEqual(
            ["network", "project-files", "user-files"],
            setup["install-project-dependencies"]["mutations"],
        )

    def test_frontend_dependency_state_requires_installed_package_contents(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            frontend = Path(directory)
            package = {
                "version": "1.2.3",
                "resolved": "https://registry.example.invalid/pkg-1.2.3.tgz",
                "integrity": "sha512-test",
            }
            lock = {"packages": {"": {}, "node_modules/pkg": package}}
            node_modules = frontend / "node_modules"
            package_dir = node_modules / "pkg"
            package_dir.mkdir(parents=True)
            (frontend / "package-lock.json").write_text(
                json.dumps(lock), encoding="utf-8"
            )
            (node_modules / ".package-lock.json").write_text(
                json.dumps(lock), encoding="utf-8"
            )
            (package_dir / "package.json").write_text(
                json.dumps({"name": "pkg", "version": "1.2.3"}),
                encoding="utf-8",
            )
            with mock.patch.object(axis, "FRONTEND_DIR", frontend):
                self.assertEqual([], axis.frontend_dependency_state_issues())
                (package_dir / "package.json").unlink()
                package_dir.rmdir()
                self.assertIn(
                    "frontend dependency contents are missing: node_modules/pkg",
                    axis.frontend_dependency_state_issues(),
                )

    def test_plan_only_prints_selected_steps_without_running_checks(self) -> None:
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/package-lock.json", "scripts/axis.py"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check") as text_encoding,
            mock.patch.object(axis, "check_frontend_toolchain") as toolchain,
            mock.patch.object(axis, "check_frontend_dependency_versions") as versions,
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
        for patched in (text_encoding, toolchain, versions, audit, frontend, scripts_standard, policy_tests):
            patched.assert_not_called()

    def test_package_manifest_change_runs_dependency_gates_without_unit_suite(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=(
                    "working tree",
                    ["frontend/package.json", "frontend/package-lock.json"],
                ),
            ),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
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

        self.assertEqual(["toolchain", "versions", "audit", "ci"], calls)

    def test_risk_acceptance_manifest_change_runs_frontend_vulnerability_gate(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/dependency-risk-acceptances.json"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
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
            mock.patch.object(axis, "run_frontend_npm") as related_tests,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(["toolchain", "versions", "audit", "ci"], calls)
        related_tests.assert_not_called()

    def test_frontend_source_change_runs_version_and_vulnerability_gates(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/src/main.tsx"]),
            ),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
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
            mock.patch.object(
                axis,
                "run_frontend_npm",
                side_effect=lambda _args: (
                    calls.append("test")
                    or axis.subprocess.CompletedProcess([], 0)
                ),
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(["toolchain", "versions", "audit", "ci", "test"], calls)

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
    def test_review_checks_parser_accepts_checkpoint_scope(self) -> None:
        args = axis.build_parser().parse_args(["review-checks", "--since", "checkpoint"])

        self.assertEqual("checkpoint", args.since)

    def test_review_checks_parser_accepts_supplemental_profile(self) -> None:
        args = axis.build_parser().parse_args(["review-checks", "--supplemental"])

        self.assertTrue(args.supplemental)

    def test_policy_tests_default_to_full_discovery(self) -> None:
        completed = axis.subprocess.CompletedProcess([], 0)

        with mock.patch.object(axis, "run", return_value=completed) as run:
            self.assertEqual(0, axis.check_policy_tests())

        run.assert_called_once_with(
            [
                axis.sys.executable,
                "-m",
                "unittest",
                "discover",
                "-s",
                "scripts/tests",
                "-p",
                "test_*.py",
            ],
            check=False,
        )

    def test_policy_tests_can_select_only_named_cases(self) -> None:
        completed = axis.subprocess.CompletedProcess([], 0)
        args = axis.argparse.Namespace(tests=["tests.Example.test_one", "tests.Example.test_two"])

        with mock.patch.object(axis, "run", return_value=completed) as run:
            self.assertEqual(0, axis.check_policy_tests(args))

        run.assert_called_once_with(
            [
                axis.sys.executable,
                "-m",
                "unittest",
                "tests.Example.test_one",
                "tests.Example.test_two",
            ],
            check=False,
        )

    def test_runs_verify_and_shared_policy_profile(self) -> None:
        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["frontend/src/App.tsx"])),
            mock.patch.object(axis, "verify", return_value=0) as verify,
            mock.patch.object(axis, "run_review_checks_policy", return_value=(0, ["doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.review_checks(
                axis.argparse.Namespace(since=None, policy_only=False)
            )

        self.assertEqual(0, result)
        verify.assert_called_once()
        policy.assert_called_once_with(
            ["frontend/src/App.tsx"],
            policy_tests_covered=False,
            doc_drift_covered=set(),
            doc_drift_range=None,
        )

    def test_policy_only_uses_same_profile_without_verify(self) -> None:
        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["scripts/axis.py"])),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_review_checks_policy", return_value=(0, ["policy gate tests", "doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.review_checks(
                axis.argparse.Namespace(since=None, policy_only=True)
            )

        self.assertEqual(0, result)
        verify.assert_not_called()
        policy.assert_called_once_with(
            ["scripts/axis.py"],
            policy_tests_covered=False,
            doc_drift_covered=set(),
            doc_drift_range=None,
        )

    def test_supplemental_profile_reuses_required_development_coverage(self) -> None:
        paths = ["scripts/axis.py", "docs/playbooks/scripts.md"]
        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", paths)),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_review_checks_policy", return_value=(0, ["doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.review_checks(
                axis.argparse.Namespace(since=None, policy_only=False, supplemental=True)
            )

        self.assertEqual(0, result)
        verify.assert_not_called()
        policy.assert_called_once_with(
            paths,
            policy_tests_covered=True,
            doc_drift_covered=axis.review_checks_doc_drift_coverage(paths),
            doc_drift_range=None,
        )

    def test_policy_registry_routes_only_triggered_expensive_checks(self) -> None:
        names = [
            name
            for name, _checker in axis.review_checks_policy_gates(
                ["scripts/axis.py", ".github/renovate.json5"]
            )
        ]

        self.assertEqual(["policy gate tests", "Renovate config", "doc drift"], names)
        self.assertEqual(
            ["doc drift"],
            [
                name
                for name, _checker in axis.review_checks_policy_gates(
                    ["frontend/src/App.tsx"],
                    policy_tests_covered=True,
                )
            ],
        )

    def test_review_checks_reuses_verify_coverage_in_doc_drift(self) -> None:
        paths = [
            "scripts/axis.py",
            ".agents/skills/run-project-command/SKILL.md",
            "docs/use-cases/example.md",
            "docs/foundations/example.md",
        ]

        self.assertEqual(
            {
                "check-text-encoding",
                "check-scripts-standard",
                "check-doc-navigation",
                "check-doc-size-budgets",
                "check-doc-code-fences.py",
                "check-use-case-docs.py",
                "check-foundation-docs.py",
            },
            axis.review_checks_doc_drift_coverage(paths),
        )
        self.assertIn(
            "check-theme",
            axis.review_checks_doc_drift_coverage(["theme/axis-theme.json"]),
        )

    def test_doc_drift_selects_only_checkers_for_touched_surfaces(self) -> None:
        selected = axis.doc_drift_checker_names(
            [
                "scripts/axis.py",
                "docs/playbooks/scripts.md",
            ]
        )

        self.assertEqual(
            {
                "check-text-encoding",
                "check-scripts-standard",
                "check-doc-link-targets.py",
                "check-doc-navigation",
                "check-doc-size-budgets",
                "check-doc-code-fences.py",
            },
            selected,
        )
        self.assertFalse(
            {
                "check-ef-domain-mapping",
                "check-frontend-api-contracts",
                "check-ui-baseline",
                "check-theme",
                "check-frontend-quality",
                "check-use-case-docs.py",
                "check-foundation-docs.py",
                "check-local-dev-docs.py",
            }
            & selected
        )

    def test_doc_drift_gate_receives_covered_checks(self) -> None:
        covered = {"check-doc-navigation"}
        gates = dict(
            axis.review_checks_policy_gates(
                ["docs/playbooks/scripts.md"],
                doc_drift_covered=covered,
                doc_drift_range="base..HEAD",
            )
        )

        with mock.patch.object(axis, "check_doc_drift", return_value=0) as doc_drift:
            self.assertEqual(0, gates["doc drift"]())

        args = doc_drift.call_args.args[0]
        self.assertEqual(covered, args.skip_checkers)
        self.assertEqual(["docs/playbooks/scripts.md"], args.paths)
        self.assertEqual("base..HEAD", args.range_spec)

    def test_pre_push_full_delegates_to_review_checks(self) -> None:
        with (
            mock.patch.dict(axis.os.environ, {"AXIS_PRE_PUSH_FULL": "1"}),
            mock.patch.object(axis, "review_checks", return_value=0) as review_checks,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.pre_push(object())

        self.assertEqual(0, result)
        review_checks.assert_called_once()
        delegated = review_checks.call_args.args[0]
        self.assertIsNone(delegated.since)
        self.assertFalse(delegated.policy_only)

    def test_pre_push_quick_gate_validates_publish_metadata(self) -> None:
        with (
            mock.patch.dict(axis.os.environ, {}, clear=True),
            mock.patch.object(axis, "diff_range", return_value="base...HEAD"),
            mock.patch.object(axis, "changed_paths", return_value=["frontend/src/App.tsx"]),
            mock.patch.object(axis, "check_publish_metadata", return_value=0) as publish_metadata,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.pre_push(object())

        self.assertEqual(0, result)
        publish_metadata.assert_called_once()
        args = publish_metadata.call_args.args[0]
        self.assertIsNone(args.branch)
        self.assertEqual("base...HEAD", args.range_spec)

    def test_pre_push_hook_rejects_invalid_remote_branch_instead_of_current_branch(self) -> None:
        update = (
            f"refs/heads/fix/local {'a' * 40} "
            f"refs/heads/automation/invalid {'0' * 40}\n"
        )
        with (
            mock.patch.object(axis.sys, "stdin", io.StringIO(update)),
            mock.patch.object(
                axis,
                "publication_branch_issues",
                return_value=["invalid automation branch"],
            ),
            mock.patch.object(axis, "diff_range", return_value="current-base...HEAD"),
            mock.patch.object(axis, "changed_paths", return_value=["frontend/src/App.tsx"]),
            mock.patch.object(axis, "check_publish_metadata", return_value=0),
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            result = axis.pre_push(axis.argparse.Namespace(updates_from_stdin=True))

        self.assertEqual(1, result)
        self.assertIn("automation/invalid", stderr.getvalue())

    def test_pre_push_hook_checks_the_pushed_sha_and_remote_branch(self) -> None:
        pushed_sha = "a" * 40
        update = (
            f"refs/heads/fix/local {pushed_sha} "
            f"refs/heads/fix/remote-target {'b' * 40}\n"
        )
        with (
            mock.patch.object(axis.sys, "stdin", io.StringIO(update)),
            mock.patch.object(axis, "publication_branch_issues", return_value=[]),
            mock.patch.object(
                axis,
                "publish_range_for_commit",
                return_value=f"base...{pushed_sha}",
                create=True,
            ) as publish_range,
            mock.patch.object(
                axis,
                "changed_paths_for_publish_ranges",
                return_value=["frontend/src/App.tsx"],
                create=True,
            ),
            mock.patch.object(axis, "check_publish_metadata", return_value=0) as publish_metadata,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.pre_push(axis.argparse.Namespace(updates_from_stdin=True))

        self.assertEqual(0, result)
        publish_range.assert_called_once_with(pushed_sha)
        publish_args = publish_metadata.call_args.args[0]
        self.assertEqual("fix/remote-target", publish_args.branch)
        self.assertEqual(f"base...{pushed_sha}", publish_args.range_spec)

    def test_pre_push_hook_ignores_tag_updates_and_branch_deletions(self) -> None:
        updates = (
            f"refs/tags/v1 {'a' * 40} refs/tags/v1 {'0' * 40}\n"
            f"(delete) {'0' * 40} refs/heads/fix/old {'b' * 40}\n"
        )

        self.assertEqual([], axis.parse_pre_push_branch_updates(updates))

    def test_pre_push_full_refuses_a_pushed_sha_other_than_checked_out_head(self) -> None:
        pushed_sha = "a" * 40
        update = (
            f"refs/heads/fix/local {pushed_sha} "
            f"refs/heads/fix/remote-target {'b' * 40}\n"
        )
        with (
            mock.patch.dict(axis.os.environ, {"AXIS_PRE_PUSH_FULL": "1"}, clear=True),
            mock.patch.object(axis.sys, "stdin", io.StringIO(update)),
            mock.patch.object(axis, "publication_branch_issues", return_value=[]),
            mock.patch.object(
                axis,
                "publish_range_for_commit",
                return_value=f"base...{pushed_sha}",
            ),
            mock.patch.object(axis, "check_publish_metadata", return_value=0),
            mock.patch.object(axis, "git", return_value=f"{'c' * 40}\n"),
            mock.patch.object(axis, "review_checks") as review_checks,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            result = axis.pre_push(axis.argparse.Namespace(updates_from_stdin=True))

        self.assertEqual(1, result)
        self.assertIn("checked-out HEAD", stderr.getvalue())
        review_checks.assert_not_called()

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
            elif args[1:5] == ["exec", "--", "vitest", "related"]:
                calls.append(" ".join(args[1:]))
            else:
                calls.append(" ".join(args[:3]))
            return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/src/main.tsx"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
            mock.patch.object(
                axis,
                "check_frontend_vulnerable_packages",
                side_effect=lambda: calls.append("audit") or 0,
            ),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "npm_cli_command", return_value=["npm"]),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "frontend-toolchain",
                "versions",
                "audit",
                "frontend-toolchain",
                "npm run ci",
                "exec -- vitest related src/main.tsx --run",
            ],
            calls,
        )

    def test_runs_only_changed_frontend_test_file_for_test_only_change(self) -> None:
        calls: list[str] = []

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/tests/button.test.tsx"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
            mock.patch.object(
                axis,
                "check_frontend_vulnerable_packages",
                side_effect=lambda: calls.append("audit") or 0,
            ),
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
                "versions",
                "audit",
                "frontend-toolchain",
                "run ci",
                "exec vitest run tests/button.test.tsx",
            ],
            calls,
        )

    def test_runs_full_frontend_suite_for_test_runtime_change(self) -> None:
        calls: list[str] = []

        def fake_run(args: list[str], **_kwargs):
            if args[1:3] == ["run", "ci"]:
                calls.append("npm run ci")
            elif args[1:3] == ["run", "test"]:
                calls.append("npm run test")
            return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")

        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/src/test/setup.ts"]),
            ),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(axis, "check_frontend_dependency_versions", side_effect=lambda: calls.append("versions") or 0),
            mock.patch.object(axis, "check_frontend_vulnerable_packages", side_effect=lambda: calls.append("audit") or 0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "npm_cli_command", return_value=["npm"]),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(
            [
                "frontend-toolchain",
                "versions",
                "audit",
                "frontend-toolchain",
                "npm run ci",
                "frontend-toolchain",
                "npm run test",
            ],
            calls,
        )

    def test_runs_changed_frontend_e2e_file_with_recorded_topology_for_e2e_only_change(self) -> None:
        calls: list[str] = []
        browser_runner = mock.Mock(return_value=0)
        product_overlay = Path("/workspace/product.compose.yml")

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", ["frontend/e2e/register.pw.ts"])),
            mock.patch.object(axis, "check_frontend_toolchain", side_effect=lambda: calls.append("frontend-toolchain") or 0),
            mock.patch.object(
                axis,
                "check_frontend_dependency_versions",
                side_effect=lambda: calls.append("versions") or 0,
            ),
            mock.patch.object(
                axis,
                "check_frontend_vulnerable_packages",
                side_effect=lambda: calls.append("audit") or 0,
            ),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "read_local_dev_topology", return_value=(product_overlay,)),
            mock.patch.object(axis, "run_local_dev_browser", browser_runner),
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
                "versions",
                "audit",
                "frontend-toolchain",
                "run ci",
            ],
            calls,
        )
        browser_runner.assert_called_once_with(
            ["e2e/register.pw.ts"],
            overlays=(product_overlay,),
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

    def test_runs_only_changed_dotnet_test_class_without_source_change(self) -> None:
        project = "tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj"
        changed_test = "tests/Api/Axis.Api.Tests/Rules/RuleDefinitionEndpointTests.cs"

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", [changed_test])),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "check_test_naming", return_value=0),
            mock.patch.object(axis, "dotnet_format_changed_paths", return_value=0),
            mock.patch.object(axis, "dotnet_test_projects", return_value=0) as dotnet_test_projects,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        dotnet_test_projects.assert_called_once_with(
            [project],
            {project: ["RuleDefinitionEndpointTests"]},
        )

    def test_source_change_uses_changed_test_classes_as_focused_proof(self) -> None:
        api_tests = "tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj"
        architecture_tests = "tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj"
        paths = [
            "src/Axis.Api/Endpoints/ProductRoleAssignmentEndpoints.cs",
            "tests/Api/Axis.Api.Tests/Authorization/ProductRoleAssignmentEndpointTests.cs",
        ]

        with (
            mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", paths)),
            mock.patch.object(axis, "run_text_encoding_check", return_value=0),
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "check_test_naming", return_value=0),
            mock.patch.object(axis, "dotnet_build_projects", return_value=0),
            mock.patch.object(axis, "dotnet_format_changed_paths", return_value=0),
            mock.patch.object(axis, "dotnet_test_projects", return_value=0) as dotnet_test_projects,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        dotnet_test_projects.assert_called_once_with(
            [api_tests, architecture_tests],
            {api_tests: ["ProductRoleAssignmentEndpointTests"]},
        )

    def test_generated_api_contract_delta_uses_openapi_owner_test(self) -> None:
        api_tests = "tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj"
        architecture_tests = "tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj"
        paths = ["openapi.json", "src/Axis.Api/appsettings.json"]

        build_projects, test_projects = axis.dotnet_projects_for_changed_paths(paths)

        self.assertEqual(["src/Axis.Api/Axis.Api.csproj"], build_projects)
        self.assertEqual([api_tests, architecture_tests], test_projects)
        self.assertEqual(
            {api_tests: ["OpenApiDocumentTests"]},
            axis.dotnet_test_class_filters(paths, test_projects),
        )

    def test_prunes_changed_build_projects_already_built_by_selected_root(self) -> None:
        api = "src/Axis.Api/Axis.Api.csproj"
        shared_domain = "src/Shared/Axis.Shared.Domain/Axis.Shared.Domain.csproj"

        self.assertEqual(
            [api],
            axis.minimal_dotnet_build_projects([shared_domain, api]),
        )

    def test_maps_mcp_source_to_mcp_contract_tests(self) -> None:
        self.assertEqual(
            "tests/Tools/Axis.Mcp.Tests/Axis.Mcp.Tests.csproj",
            axis.related_test_project_for_source_project("src/Axis.Mcp/Axis.Mcp.csproj"),
        )

    def test_mcp_source_and_openapi_changes_run_api_coverage(self) -> None:
        for changed_path in ("src/Axis.Mcp/Tools/AxisMcpTool.cs", "openapi.json"):
            with self.subTest(changed_path=changed_path):
                calls: list[str] = []
                with (
                    mock.patch.object(
                        axis,
                        "verify_scope_paths",
                        return_value=("working tree", [changed_path]),
                    ),
                    mock.patch.object(axis, "run_text_encoding_check", return_value=0),
                    mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
                    mock.patch.object(axis, "dotnet_build_projects", return_value=0),
                    mock.patch.object(axis, "dotnet_format_changed_paths", return_value=0),
                    mock.patch.object(axis, "dotnet_test_projects", return_value=0),
                    mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                    mock.patch.object(axis, "check_frontend_dependency_versions", return_value=0),
                    mock.patch.object(axis, "check_frontend_vulnerable_packages", return_value=0),
                    mock.patch.object(axis, "frontend_command", return_value=0),
                    mock.patch.object(
                        axis,
                        "check_mcp_api_coverage",
                        side_effect=lambda: calls.append("mcp-api-coverage") or 0,
                    ),
                    mock.patch.object(
                        axis,
                        "check_mcp_contracts",
                        side_effect=lambda: calls.append("mcp-contracts") or 0,
                    ),
                    mock.patch.object(
                        axis,
                        "check_mcp_tool_safety",
                        side_effect=lambda: calls.append("mcp-tool-safety") or 0,
                    ),
                    contextlib.redirect_stdout(io.StringIO()),
                ):
                    self.assertEqual(0, axis.verify(object()))

                expected = ["mcp-api-coverage"]
                if changed_path.startswith("src/Axis.Mcp/"):
                    expected.extend(["mcp-contracts", "mcp-tool-safety"])
                self.assertEqual(expected, calls)

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

    def test_current_repository_enforcement_ledger_still_passes(self) -> None:
        self.assertEqual([], axis.enforcement_ledger_issues())


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

    def test_allows_design_gate_playbook_and_rejects_per_change_dossier(self) -> None:
        self.assertEqual(
            [],
            self.issues_for_files({"docs/playbooks/design-gate.md": "# Design Gate\n"}),
        )

        issues = self.issues_for_files(
            {"docs/playbooks/design-gate-standard-work.md": "# Design Gate\n"}
        )

        self.assertIn(
            "per-change Design Gate dossiers belong in the active task handoff",
            "\n".join(issues),
        )

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

    def test_pre_push_hook_forwards_git_update_records_to_axis(self) -> None:
        hook = (ROOT / "scripts" / "hooks" / "pre-push").read_text(encoding="utf-8")

        self.assertIn('"pre-push"', hook)
        self.assertIn('"--updates-from-stdin"', hook)

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
    def setUp(self) -> None:
        self.real_discover_local_dev_compose_overlays = (
            axis.discover_local_dev_compose_overlays
        )
        self.topology_temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.topology_temp.cleanup)
        self.topology_root = Path(self.topology_temp.name)
        self.topology_state = self.topology_root / "local-dev-topology.json"
        topology_state = mock.patch.object(
            axis,
            "LOCAL_DEV_TOPOLOGY_STATE",
            self.topology_state,
        )
        topology_state.start()
        self.addCleanup(topology_state.stop)
        topology_discovery = mock.patch.object(
            axis,
            "discover_local_dev_compose_overlays",
            return_value=None,
        )
        topology_discovery.start()
        self.addCleanup(topology_discovery.stop)
        certificate_preflight = mock.patch.object(
            axis,
            "require_local_dev_certificates",
            return_value=0,
        )
        self.certificate_preflight = certificate_preflight.start()
        self.addCleanup(certificate_preflight.stop)

    def make_overlay(self, name: str = "product.yml") -> Path:
        overlay = self.topology_root / name
        overlay.write_text("services: {}\n", encoding="utf-8")
        return overlay

    def write_topology_state(self, overlays: list[Path]) -> None:
        self.topology_state.write_text(
            json.dumps(
                {
                    "version": 1,
                    "composeOverlays": [str(overlay.resolve()) for overlay in overlays],
                }
            ),
            encoding="utf-8",
        )

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

    def test_local_dev_doc_check_requires_api_source_reload_capability(self) -> None:
        one_shot_compose = """services:
  api:
    command: [\"dotnet\", \"Axis.Api.dll\"]
    environment:
      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"
    ports:
      - \"127.0.0.1:5281:8443\"
"""
        watching_compose = """services:
  api:
    command:
      - dotnet
      - watch
      - --project
      - src/Axis.Api/Axis.Api.csproj
      - --no-hot-reload
      - run
    environment:
      DOTNET_USE_POLLING_FILE_WATCHER: \"true\"
      UseArtifactsOutput: \"true\"
      ArtifactsPath: \"/tmp/axis-artifacts\"
      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"
    ports:
      - \"127.0.0.1:5281:8443\"
"""

        cli_artifacts_compose = """services:
  api:
    command:
      - dotnet
      - watch
      - --project
      - src/Axis.Api/Axis.Api.csproj
      - --no-hot-reload
      - run
      - --artifacts-path
      - /tmp/axis-artifacts
    environment:
      DOTNET_USE_POLLING_FILE_WATCHER: \"true\"
      UseArtifactsOutput: \"true\"
      ArtifactsPath: \"/tmp/axis-artifacts\"
      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"
    ports:
      - \"127.0.0.1:5281:8443\"
"""

        cli_artifacts_equals_compose = watching_compose.replace(
            "      - run\n",
            "      - run\n      - --ARTIFACTS-PATH=/tmp/axis-artifacts\n",
        )

        indirect_compose = """services:
  api:
    command: [\"sh\", \"-c\", \"dotnet watch --project src/Axis.Api/Axis.Api.csproj run\"]
    environment:
      DOTNET_USE_POLLING_FILE_WATCHER: \"true\"
      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"
    ports:
      - \"127.0.0.1:5281:8443\"
"""
        unquoted_flow_compose = """services:
  api:
    command: [dotnet, watch, --project, src/Axis.Api/Axis.Api.csproj, run]
    environment:
      DOTNET_USE_POLLING_FILE_WATCHER: \"true\"
      UseArtifactsOutput: \"true\"
      ArtifactsPath: \"/tmp/axis-artifacts\"
      App__BaseUrl: \"${APP_BASE_URL:-https://localhost:3000}\"
    ports:
      - \"127.0.0.1:5281:8443\"
"""

        def check(compose_text: str) -> list[str]:
            with tempfile.TemporaryDirectory() as temp:
                compose = Path(temp) / "docker-compose.yml"
                compose.write_text(compose_text, encoding="utf-8")
                with mock.patch.object(check_local_dev_docs, "MAIN_COMPOSE_FILE", compose):
                    return check_local_dev_docs.check_local_dev_doc()

        expected = "docker-compose.yml api service must automatically reload source changes"
        self.assertIn(expected, check(one_shot_compose))
        self.assertIn(expected, check(indirect_compose))
        self.assertIn(expected, check(cli_artifacts_compose))
        self.assertIn(expected, check(cli_artifacts_equals_compose))
        self.assertIn(
            expected,
            check(watching_compose.replace('      UseArtifactsOutput: "true"\n', "")),
        )
        self.assertIn(
            expected,
            check(watching_compose.replace("/tmp/axis-artifacts", "/src/artifacts")),
        )
        self.assertNotIn(expected, check(watching_compose))
        self.assertNotIn(expected, check(unquoted_flow_compose))

    def test_local_dev_doc_check_requires_trusted_web_https_healthcheck(self) -> None:
        canonical = """services:
  web:
    environment:
      NODE_EXTRA_CA_CERTS: "/https/rootCA.pem"
    healthcheck:
      test: ["CMD", "node", "-e", "fetch('https://localhost:3000').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))"]
    ports:
      - "127.0.0.1:3000:3000"
"""
        insecure = canonical.replace(
            "[\"CMD\", \"node\", \"-e\", \"fetch('https://localhost:3000').then(r => process.exit(r.ok ? 0 : 1)).catch(() => process.exit(1))\"]",
            "[\"CMD-SHELL\", \"wget --no-check-certificate https://localhost:3000\"]",
        )

        def check(compose_text: str) -> list[str]:
            with tempfile.TemporaryDirectory() as temp:
                compose = Path(temp) / "docker-compose.yml"
                compose.write_text(compose_text, encoding="utf-8")
                with mock.patch.object(check_local_dev_docs, "MAIN_COMPOSE_FILE", compose):
                    return check_local_dev_docs.check_local_dev_doc()

        expected = "docker-compose.yml web service must expose a trusted HTTPS healthcheck"
        self.assertNotIn(expected, check(canonical))
        self.assertIn(expected, check(insecure))

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

    def test_api_appsettings_openiddict_issuer_reads_canonical_issuer(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            appsettings = Path(temp) / "appsettings.json"
            appsettings.write_text(
                '{"OpenIddict": {"Issuer": "https://localhost:5281"}}',
                encoding="utf-8",
            )

            self.assertEqual(
                "https://localhost:5281",
                check_local_dev_docs.api_appsettings_openiddict_issuer(appsettings),
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
            mock.patch.object(
                axis,
                "local_dev_host_trust_status",
                return_value=("OK", "Axis local root CA is trusted for this host user"),
                create=True,
            ),
            mock.patch.object(axis, "run", side_effect=fake_run),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.local_dev(args))

        return calls

    def run_local_dev_with_codes(
        self,
        args: axis.argparse.Namespace,
        *return_codes: int,
    ) -> tuple[int, list[list[str]]]:
        calls: list[list[str]] = []
        codes = iter(return_codes)

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(
                command,
                next(codes),
                stdout="",
                stderr="",
            )

        with (
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            return axis.local_dev(args), calls

    def test_up_uses_axis_project_and_committed_compose_file(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="up", build=False, services=[])
        )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "up",
                "-d",
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[0][1:],
        )

    def test_https_service_selection_uses_shared_certificate_preflight(self) -> None:
        for services in ([], ["api"], ["web"], ["e2e"], ["postgres", "api"]):
            with self.subTest(services=services):
                self.assertTrue(axis.local_dev_services_require_certificates(services))
        for services in (["postgres"], ["redis"], ["maildev"], ["otel-lgtm"]):
            with self.subTest(services=services):
                self.assertFalse(axis.local_dev_services_require_certificates(services))

    def test_up_preflights_only_when_selected_services_require_https(self) -> None:
        self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="up", build=False, services=[])
        )
        self.certificate_preflight.assert_called_once_with("local-dev up")

        self.certificate_preflight.reset_mock()
        self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="up",
                build=False,
                services=["postgres"],
            )
        )
        self.certificate_preflight.assert_not_called()

    def test_browser_runner_stops_before_docker_when_certificate_preflight_fails(self) -> None:
        with (
            mock.patch.object(axis, "require_local_dev_certificates", return_value=1) as preflight,
            mock.patch.object(axis, "run") as run,
        ):
            self.assertEqual(1, axis.run_local_dev_browser(["e2e/sample.pw.ts"]))

        preflight.assert_called_once_with("local-dev e2e")
        run.assert_not_called()

    def test_reset_stops_before_destructive_docker_calls_when_certificate_preflight_fails(self) -> None:
        with (
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "require_local_dev_certificates", return_value=1),
            mock.patch.object(axis, "run") as run,
        ):
            self.assertEqual(
                1,
                axis.local_dev(
                    axis.argparse.Namespace(local_dev_command="reset-db", yes=True)
                ),
            )

        run.assert_not_called()

    def test_up_applies_explicit_compose_overlays_in_order(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            first = Path(temp) / "product.yml"
            second = Path(temp) / "environment.yaml"
            first.write_text("services: {}\n", encoding="utf-8")
            second.write_text("services: {}\n", encoding="utf-8")

            calls = self.run_local_dev(
                axis.argparse.Namespace(
                    local_dev_command="up",
                    build=False,
                    services=[],
                    compose_overlays=[str(first), str(second)],
                )
            )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "-f",
                str(first),
                "-f",
                str(second),
                "up",
                "-d",
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[0][1:],
        )

    def test_compose_overlay_rejects_missing_non_yaml_and_duplicate_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            text_file = root / "overlay.txt"
            text_file.write_text("services: {}\n", encoding="utf-8")
            yaml_file = root / "overlay.yml"
            yaml_file.write_text("services: {}\n", encoding="utf-8")

            with self.assertRaisesRegex(axis.CheckError, "does not exist"):
                axis.resolve_local_dev_compose_overlays([str(root / "missing.yml")])
            with self.assertRaisesRegex(axis.CheckError, "must be a .yml or .yaml file"):
                axis.resolve_local_dev_compose_overlays([str(text_file)])
            with self.assertRaisesRegex(axis.CheckError, "is duplicated"):
                axis.resolve_local_dev_compose_overlays([str(yaml_file), str(yaml_file)])

    def test_invalid_compose_overlay_fails_before_docker_is_invoked(self) -> None:
        with mock.patch.object(axis, "_docker_compose_ok") as docker_compose_ok:
            with self.assertRaisesRegex(axis.CheckError, "does not exist"):
                axis.local_dev(
                    axis.argparse.Namespace(
                        local_dev_command="up",
                        build=False,
                        services=[],
                        compose_overlays=["missing.yml"],
                    )
                )

        docker_compose_ok.assert_not_called()

    def test_overlay_topology_survives_down_and_blocks_bare_reconciliation_before_docker(self) -> None:
        overlay = self.make_overlay()
        overlay_args = [str(overlay)]
        self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="up",
                build=False,
                services=[],
                compose_overlays=overlay_args,
            )
        )
        self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="down",
                volumes=False,
                compose_overlays=overlay_args,
            )
        )
        self.assertEqual(
            {"version": 1, "composeOverlays": [str(overlay.resolve())]},
            json.loads(self.topology_state.read_text(encoding="utf-8")),
        )
        self.run_local_dev(axis.argparse.Namespace(local_dev_command="status"))
        self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="logs", follow=False, services=[])
        )

        bare_commands = (
            axis.argparse.Namespace(local_dev_command="up", build=False, services=[]),
            axis.argparse.Namespace(local_dev_command="e2e", e2e_args=[]),
            axis.argparse.Namespace(local_dev_command="recreate", services=["api"]),
        )
        for args in bare_commands:
            with (
                self.subTest(command=args.local_dev_command),
                mock.patch.object(axis, "_docker_compose_ok") as docker_compose_ok,
                mock.patch.object(axis, "run") as run,
                self.assertRaisesRegex(axis.CheckError, "deployment topology mismatch"),
            ):
                axis.local_dev(args)
            docker_compose_ok.assert_not_called()
            run.assert_not_called()

    def test_matching_ordered_overlay_topology_is_allowed(self) -> None:
        first = self.make_overlay()
        second = self.make_overlay("environment.yaml")
        self.write_topology_state([first, second])
        calls = self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="up",
                build=False,
                services=[],
                compose_overlays=[str(first), str(second)],
            )
        )
        self.assertEqual([str(first), "-f", str(second)], calls[0][7:10])

        with (
            mock.patch.object(axis, "_docker_compose_ok") as docker_compose_ok,
            mock.patch.object(axis, "run") as run,
            self.assertRaisesRegex(axis.CheckError, "deployment topology mismatch"),
        ):
            axis.local_dev(
                axis.argparse.Namespace(
                    local_dev_command="up",
                    build=False,
                    services=[],
                    compose_overlays=[str(second), str(first)],
                )
            )
        docker_compose_ok.assert_not_called()
        run.assert_not_called()

    def test_active_overlay_topology_is_adopted_before_bare_reconciliation(self) -> None:
        overlay = self.make_overlay()
        with (
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(
                axis,
                "discover_local_dev_compose_overlays",
                return_value=(overlay.resolve(),),
            ),
            mock.patch.object(axis, "run") as run,
            self.assertRaisesRegex(axis.CheckError, "deployment topology mismatch"),
        ):
            axis.local_dev(
                axis.argparse.Namespace(
                    local_dev_command="up",
                    build=False,
                    services=[],
                )
            )

        run.assert_not_called()
        self.assertTrue(self.topology_state.is_file())

    def test_base_topology_allows_the_first_explicit_overlay_claim(self) -> None:
        overlay = self.make_overlay()
        with mock.patch.object(
            axis,
            "discover_local_dev_compose_overlays",
            return_value=(),
        ):
            self.run_local_dev(
                axis.argparse.Namespace(
                    local_dev_command="up",
                    build=False,
                    services=[],
                    compose_overlays=[str(overlay)],
                )
            )

        self.assertEqual((overlay.resolve(),), axis.read_local_dev_topology())

    def test_failed_first_overlay_up_does_not_claim_topology(self) -> None:
        overlay = self.make_overlay()
        result, _ = self.run_local_dev_with_codes(
            axis.argparse.Namespace(
                local_dev_command="up",
                build=False,
                services=[],
                compose_overlays=[str(overlay)],
            ),
            1,
        )

        self.assertEqual(1, result)
        self.assertFalse(self.topology_state.exists())

    def test_e2e_claims_first_overlay_only_after_stack_up_succeeds(self) -> None:
        overlay = self.make_overlay()

        def run_e2e(build_services: list[str], *return_codes: int) -> int:
            result, _ = self.run_local_dev_with_codes(
                axis.argparse.Namespace(
                    local_dev_command="e2e",
                    build_services=build_services,
                    e2e_args=[],
                    compose_overlays=[str(overlay)],
                ),
                *return_codes,
            )
            return result

        self.assertEqual(1, run_e2e(["api"], 1))
        self.assertFalse(self.topology_state.exists())

        self.assertEqual(1, run_e2e([], 0, 1))
        self.assertEqual((overlay.resolve(),), axis.read_local_dev_topology())

    def test_active_overlay_topology_is_read_from_compose_container_metadata(self) -> None:
        overlay = self.topology_root / "product.yml"
        result = axis.subprocess.CompletedProcess(
            [],
            0,
            stdout=f"{axis.LOCAL_DEV_COMPOSE_FILE},{overlay}\n",
            stderr="",
        )
        with mock.patch.object(axis, "run_optional", return_value=result) as run_optional:
            self.assertEqual(
                (overlay.resolve(),),
                self.real_discover_local_dev_compose_overlays(),
            )

        self.assertEqual("inspect", run_optional.call_args.args[0][1])
        missing = axis.subprocess.CompletedProcess(
            [],
            1,
            stdout="",
            stderr="error: no such object: axis_api",
        )
        with mock.patch.object(axis, "run_optional", return_value=missing):
            self.assertIsNone(self.real_discover_local_dev_compose_overlays())

    def test_relocated_base_topology_is_adopted_only_without_overlays(self) -> None:
        relocated_base = self.topology_root / "moved" / axis.LOCAL_DEV_COMPOSE_FILE.name
        base_only = axis.subprocess.CompletedProcess(
            [],
            0,
            stdout=f"{relocated_base}\n",
            stderr="",
        )
        with mock.patch.object(axis, "run_optional", return_value=base_only):
            self.assertEqual((), self.real_discover_local_dev_compose_overlays())

        with_overlay = axis.subprocess.CompletedProcess(
            [],
            0,
            stdout=f"{relocated_base},{self.make_overlay()}\n",
            stderr="",
        )
        with (
            mock.patch.object(axis, "run_optional", return_value=with_overlay),
            self.assertRaisesRegex(axis.CheckError, "topology metadata is invalid"),
        ):
            self.real_discover_local_dev_compose_overlays()

    def test_reset_all_replaces_topology_state_only_after_success(self) -> None:
        overlay = self.make_overlay()
        self.write_topology_state([overlay])
        result, calls = self.run_local_dev_with_codes(
            axis.argparse.Namespace(local_dev_command="reset-all", yes=True),
            0,
            1,
        )
        self.assertEqual(1, result)
        self.assertIn(str(overlay), calls[0])
        self.assertTrue(self.topology_state.is_file())

        self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="reset-all", yes=True)
        )
        self.assertFalse(self.topology_state.exists())

    def test_failed_first_overlay_reset_all_does_not_claim_topology(self) -> None:
        overlay = self.make_overlay()
        result, _ = self.run_local_dev_with_codes(
            axis.argparse.Namespace(
                local_dev_command="reset-all",
                yes=True,
                compose_overlays=[str(overlay)],
            ),
            0,
            1,
        )

        self.assertEqual(1, result)
        self.assertFalse(self.topology_state.exists())

    def test_down_volumes_clears_topology_only_after_success(self) -> None:
        overlay = self.make_overlay()
        self.write_topology_state([overlay])
        args = axis.argparse.Namespace(
            local_dev_command="down",
            volumes=True,
            compose_overlays=[str(overlay)],
        )

        with mock.patch.object(axis, "_docker_compose_ok", return_value=True):
            self.assertEqual(1, axis.local_dev(axis.argparse.Namespace(**vars(args), yes=False)))
        self.assertTrue(self.topology_state.is_file())

        result, _ = self.run_local_dev_with_codes(
            axis.argparse.Namespace(**vars(args), yes=True),
            1,
        )
        self.assertEqual(1, result)
        self.assertTrue(self.topology_state.is_file())

        self.run_local_dev(axis.argparse.Namespace(**vars(args), yes=True))
        self.assertFalse(self.topology_state.exists())

    def test_run_api_is_blocked_when_local_data_requires_compose_overlays(self) -> None:
        self.write_topology_state([self.make_overlay()])
        with (
            mock.patch.object(axis, "check_dotnet_sdk") as check_dotnet_sdk,
            mock.patch.object(axis, "run") as run,
            self.assertRaisesRegex(axis.CheckError, "deployment topology mismatch"),
        ):
            axis.dotnet_command(
                axis.argparse.Namespace(dotnet_command="run-api", dotnet_args=[])
            )

        check_dotnet_sdk.assert_not_called()
        run.assert_not_called()

    def test_up_reports_ready_urls_and_host_trust_followup(self) -> None:
        with (
            tempfile.TemporaryDirectory() as temp,
            mock.patch.object(axis, "LOCAL_DEV_ENV_FILE", Path(temp) / ".env.local"),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(
                axis,
                "local_dev_host_trust_status",
                return_value=(
                    "WARN",
                    "host browser trust is not configured; run "
                    "`python scripts/axis.py local-dev trust-certs`",
                ),
                create=True,
            ),
            mock.patch.object(
                axis,
                "run",
                return_value=axis.subprocess.CompletedProcess([], 0, stdout="", stderr=""),
            ),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.local_dev(
                    axis.argparse.Namespace(local_dev_command="up", build=False, services=[])
                ),
            )

        output = stdout.getvalue()
        self.assertIn("local-dev up: ready", output)
        self.assertIn("https://localhost:3000", output)
        self.assertIn("https://localhost:5281/health", output)
        self.assertIn("local-dev trust-certs", output)

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
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[0][1:],
        )

    def test_e2e_builds_and_runs_profile(self) -> None:
        calls = self.run_local_dev(axis.argparse.Namespace(local_dev_command="e2e", e2e_args=[]))

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "up",
                "-d",
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[0][1:],
        )
        self.assertEqual(
            ["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "--profile", "e2e", "build", "e2e"],
            calls[1][1:],
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
            calls[2][1:],
        )

    def test_e2e_builds_only_named_runtime_service_before_reconciling_and_running(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="e2e",
                build_services=["api"],
                e2e_args=[],
            )
        )

        self.assertEqual(["build", "api"], calls[0][-2:])
        self.assertEqual(["up", "-d", "--wait", "--wait-timeout", "300"], calls[1][-5:])
        self.assertEqual(["--profile", "e2e", "build", "e2e"], calls[2][-4:])
        self.assertEqual(["--no-deps", "e2e"], calls[3][-2:])

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
            calls[2][1:],
        )

    def test_e2e_persists_one_intentional_snapshot_update_as_the_host_user(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            frontend = Path(temp) / "frontend"
            test_path = frontend / "e2e" / "app-frame.pw.ts"
            test_path.parent.mkdir(parents=True)
            test_path.write_text("export {};\n", encoding="utf-8")
            with (
                mock.patch.object(axis, "FRONTEND_DIR", frontend),
                mock.patch.object(axis.os, "getuid", return_value=1000),
                mock.patch.object(axis.os, "getgid", return_value=1000),
            ):
                snapshot_output = axis.local_dev_snapshot_output(
                    "e2e/app-frame.pw.ts-snapshots"
                )
                calls = self.run_local_dev(
                    axis.argparse.Namespace(
                        local_dev_command="e2e",
                        service="e2e",
                        snapshot_output=snapshot_output,
                        e2e_args=[
                            "--",
                            "e2e/app-frame.pw.ts",
                            "--update-snapshots",
                        ],
                    )
                )
                self.assertTrue(snapshot_output.is_dir())
                self.assertEqual(
                    [
                        "--volume",
                        f"{snapshot_output.resolve()}:/work/frontend/e2e/app-frame.pw.ts-snapshots",
                        "--user",
                        "1000:1000",
                        "--env",
                        "HOME=/tmp/axis-e2e-home",
                        "e2e",
                        "e2e/app-frame.pw.ts",
                        "--update-snapshots",
                    ],
                    calls[2][-9:],
                )

    def test_snapshot_output_rejects_non_test_and_broad_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            frontend = Path(temp) / "frontend"
            (frontend / "e2e").mkdir(parents=True)
            with mock.patch.object(axis, "FRONTEND_DIR", frontend):
                for invalid in (
                    "e2e",
                    "e2e/snapshots",
                    "tests/app-frame.pw.ts-snapshots",
                    "e2e/missing.pw.ts-snapshots",
                ):
                    with self.subTest(invalid=invalid), self.assertRaises(
                        axis.argparse.ArgumentTypeError
                    ):
                        axis.local_dev_snapshot_output(invalid)

    def test_snapshot_output_requires_update_mode_and_axis_service(self) -> None:
        snapshot_output = axis.FRONTEND_DIR / "e2e" / "app-frame.pw.ts-snapshots"
        for service, e2e_args, expected in (
            ("e2e", ["e2e/app-frame.pw.ts"], "requires Playwright"),
            (
                "consumer-e2e",
                ["e2e/app-frame.pw.ts", "--update-snapshots"],
                "supported only by the Axis",
            ),
        ):
            with (
                self.subTest(service=service),
                mock.patch.object(axis, "_docker_compose_ok", return_value=True),
                self.assertRaisesRegex(axis.CheckError, expected),
            ):
                axis.local_dev(
                    axis.argparse.Namespace(
                        local_dev_command="e2e",
                        service=service,
                        snapshot_output=snapshot_output,
                        e2e_args=e2e_args,
                    )
                )

    def test_e2e_builds_and_runs_an_overlay_owned_verification_service(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(
                local_dev_command="e2e",
                service="consumer-e2e",
                e2e_args=["--", "tests/product.pw.ts"],
            )
        )

        self.assertEqual(
            [
                "--profile",
                "e2e",
                "build",
                "consumer-e2e",
            ],
            calls[1][-4:],
        )
        self.assertEqual(
            ["consumer-e2e", "tests/product.pw.ts"],
            calls[2][-2:],
        )

    def test_compose_service_name_rejects_shell_or_option_syntax(self) -> None:
        self.assertEqual("consumer-e2e", axis.compose_service_name("consumer-e2e"))
        for invalid in ("ConsumerE2e", "--profile", "consumer e2e", "consumer/e2e"):
            with self.subTest(invalid=invalid):
                with self.assertRaises(axis.argparse.ArgumentTypeError):
                    axis.compose_service_name(invalid)

    def test_smoke_uses_the_canonical_compose_browser_runner(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="smoke")
        )

        self.assertEqual(
            ["up", "-d", "--wait", "--wait-timeout", "300"],
            calls[0][-5:],
        )
        self.assertEqual(["--profile", "e2e", "build", "e2e"], calls[1][-4:])
        self.assertEqual(
            ["e2e", "e2e/local-dev-smoke.pw.ts"],
            calls[2][-2:],
        )
        self.certificate_preflight.assert_called_once_with("local-dev smoke")

    def test_host_smoke_executes_the_resolved_cross_platform_probe(self) -> None:
        host_smoke = getattr(axis, "local_dev_host_smoke", None)
        self.assertTrue(callable(host_smoke))
        calls: list[list[str]] = []

        def fake_run_optional(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="200\n", stderr="")

        with (
            mock.patch.object(
                axis.axis_host_https,
                "resolve_host_https_probe",
                return_value=axis.axis_host_https.HostHttpsProbe(
                    command=("host-https-client", "--verify"),
                    boundary="test host trust",
                ),
            ),
            mock.patch.object(axis, "run_optional", side_effect=fake_run_optional),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(0, host_smoke(axis.argparse.Namespace()))

        self.assertEqual([["host-https-client", "--verify"]], calls)
        self.assertIn("HTTP 200", stdout.getvalue())
        self.assertIn("test host trust", stdout.getvalue())

    def test_host_smoke_failure_reports_static_trust_remediation(self) -> None:
        host_smoke = getattr(axis, "local_dev_host_smoke", None)
        self.assertTrue(callable(host_smoke))
        with (
            mock.patch.object(
                axis.axis_host_https,
                "resolve_host_https_probe",
                return_value=axis.axis_host_https.HostHttpsProbe(
                    command=("host-https-client", "--verify"),
                    boundary="test host trust",
                ),
            ),
            mock.patch.object(
                axis,
                "run_optional",
                return_value=axis.subprocess.CompletedProcess(
                    [],
                    1,
                    stdout="",
                    stderr="certificate verify failed",
                ),
            ),
            mock.patch.object(
                axis,
                "local_dev_host_trust_status",
                return_value=(
                    "WARN",
                    "host browser trust is not configured; run `python scripts/axis.py local-dev trust-certs`",
                ),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, host_smoke(axis.argparse.Namespace()))

        self.assertIn("certificate verify failed", stderr.getvalue())
        self.assertIn("trust-certs", stderr.getvalue())

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

    def test_volume_destructive_commands_require_explicit_confirmation(self) -> None:
        cases = [
            axis.argparse.Namespace(local_dev_command="down", volumes=True, yes=False),
            axis.argparse.Namespace(local_dev_command="reset-db", yes=False),
            axis.argparse.Namespace(local_dev_command="reset-all", yes=False),
        ]
        for args in cases:
            with (
                self.subTest(command=args.local_dev_command),
                mock.patch.object(axis, "_docker_compose_ok", return_value=True),
                mock.patch.object(axis, "run") as run,
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.local_dev(args))
                run.assert_not_called()
                self.assertIn("rerun with --yes", stderr.getvalue())

    def test_reset_db_removes_postgres_volume_between_down_and_up(self) -> None:
        calls = self.run_local_dev(
            axis.argparse.Namespace(local_dev_command="reset-db", yes=True)
        )

        self.assertEqual(["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "down"], calls[0][1:])
        self.assertEqual(["volume", "rm", "axis_postgres_data"], calls[1][1:])
        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "up",
                "-d",
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[2][1:],
        )

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
            self.assertEqual(
                1,
                axis.local_dev(
                    axis.argparse.Namespace(local_dev_command="reset-db", yes=True)
                ),
            )

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
            self.assertEqual(
                0,
                axis.local_dev(
                    axis.argparse.Namespace(local_dev_command="reset-db", yes=True)
                ),
            )

        self.assertEqual(
            [
                "compose",
                "-p",
                "axis",
                "-f",
                str(axis.LOCAL_DEV_COMPOSE_FILE),
                "up",
                "-d",
                "--wait",
                "--wait-timeout",
                "300",
            ],
            calls[2][1:],
        )


class TestLocalDevShellArgv(unittest.TestCase):
    def test_defaults_by_service(self) -> None:
        self.assertEqual(["bash"], axis.local_dev_shell_argv("api", []))
        self.assertEqual(["sh"], axis.local_dev_shell_argv("web", []))
        self.assertEqual(["sh"], axis.local_dev_shell_argv("unknown", []))

    def test_strips_double_dash_prefix(self) -> None:
        self.assertEqual(["bash"], axis.local_dev_shell_argv("web", ["--", "bash"]))


class TestPublicationAuthorityAdapter(unittest.TestCase):
    def test_delegates_grammar_to_processctl_json_boundary(self) -> None:
        completed = axis.subprocess.CompletedProcess(
            [],
            1,
            stdout=json.dumps(
                {
                    "status": "failed",
                    "issues": ["invalid publication branch"],
                }
            ),
            stderr="",
        )
        with (
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            mock.patch.object(axis, "run", return_value=completed) as run,
        ):
            issues, document = axis.publication_validation(
                "validate-branch", ["--branch", "invalid"]
            )

        self.assertEqual(["invalid publication branch"], issues)
        self.assertEqual("failed", document["status"])
        self.assertEqual(
            [
                "processctl",
                "publication",
                "validate-branch",
                "--branch",
                "invalid",
                "--json",
            ],
            run.call_args.args[0],
        )
        self.assertFalse(run.call_args.kwargs["check"])


class TestGitWorkflows(unittest.TestCase):
    def setUp(self) -> None:
        super().setUp()
        branch = mock.patch.object(axis, "publication_branch_issues", return_value=[])
        publication = mock.patch.object(
            axis,
            "publication_validation",
            return_value=([], {"commits": []}),
        )
        branch.start()
        publication.start()
        self.addCleanup(publication.stop)
        self.addCleanup(branch.stop)

    def test_checkpoint_rejects_non_conventional_subject_before_git_mutation(self) -> None:
        with (
            mock.patch.object(axis, "publication_branch_issues", return_value=[]),
            mock.patch.object(
                axis,
                "publication_validation",
                return_value=(["Commit subject must use Conventional Commit style"], {}),
            ),
            mock.patch.object(axis, "working_tree_paths") as working_tree_paths,
            mock.patch.object(axis, "git_lines") as git_lines,
            mock.patch.object(axis, "git") as git,
        ):
            with self.assertRaisesRegex(axis.CheckError, "Conventional Commit"):
                axis.git_checkpoint(
                    axis.argparse.Namespace(
                        branch="fix/publish-gate",
                        subject="Harden publishing",
                        body=None,
                        all_changes=False,
                    )
                )

        working_tree_paths.assert_not_called()
        git_lines.assert_not_called()
        git.assert_not_called()

    def test_publish_metadata_rejects_non_conventional_commit_in_range(self) -> None:
        with (
            mock.patch.object(
                axis,
                "publication_validation",
                return_value=(
                    [
                        "Commit 0123456789ab: Commit subject must use "
                        "Conventional Commit style"
                    ],
                    {"commits": ["0123456789ab"]},
                ),
            ),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            result = axis.check_publish_metadata(
                axis.argparse.Namespace(
                    branch="fix/publish-gate",
                    range_spec="base..HEAD",
                )
            )

        self.assertEqual(1, result)
        self.assertIn("0123456789ab", stderr.getvalue())
        self.assertIn("Conventional Commit", stderr.getvalue())

    def test_publish_metadata_accepts_project_branch_and_commit_range(self) -> None:
        with (
            mock.patch.object(
                axis,
                "publication_validation",
                return_value=([], {"commits": ["0123456789ab"]}),
            ),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.check_publish_metadata(
                axis.argparse.Namespace(
                    branch="fix/publish-gate",
                    range_spec="base..HEAD",
                )
            )

        self.assertEqual(0, result)

    def test_cli_routes_publish_metadata_with_explicit_range(self) -> None:
        with (
            mock.patch.object(axis, "check_publish_metadata", return_value=0) as publish_metadata,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.main(
                [
                    "check",
                    "publish-metadata",
                    "--branch",
                    "fix/publish-gate",
                    "--range",
                    "base..HEAD",
                ]
            )

        self.assertEqual(0, result)
        args = publish_metadata.call_args.args[0]
        self.assertEqual("fix/publish-gate", args.branch)
        self.assertEqual("base..HEAD", args.range_spec)

    def test_repository_tracks_a_lock_file_for_every_dotnet_project(self) -> None:
        self.assertEqual([], axis.dotnet_dependency_lock_issues())

    def test_dotnet_lock_checker_rejects_missing_opt_in_and_project_lock(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "src" / "Example" / "Example.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            (root / "Directory.Packages.props").write_text(
                "<Project><PropertyGroup /></Project>\n",
                encoding="utf-8",
            )

            issues = axis.dotnet_dependency_lock_issues(root=root)

        self.assertTrue(any("RestorePackagesWithLockFile" in issue for issue in issues))
        self.assertTrue(any("packages.lock.json" in issue for issue in issues))

    def test_dotnet_lock_checker_rejects_malformed_and_orphan_locks(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "src" / "Example" / "Example.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            project.with_name("packages.lock.json").write_text("not json\n", encoding="utf-8")
            orphan = root / "tests" / "Orphan" / "packages.lock.json"
            orphan.parent.mkdir(parents=True)
            orphan.write_text('{"version": 2, "dependencies": {}}\n', encoding="utf-8")
            (root / "Directory.Packages.props").write_text(
                "<Project><PropertyGroup>"
                "<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>"
                "</PropertyGroup></Project>\n",
                encoding="utf-8",
            )

            issues = axis.dotnet_dependency_lock_issues(root=root)

        self.assertTrue(any("invalid NuGet lock file" in issue for issue in issues))
        self.assertTrue(any("no sibling .csproj owner" in issue for issue in issues))

    def test_dotnet_lock_checker_rejects_solution_inventory_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "src" / "Example" / "Example.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project />\n", encoding="utf-8")
            project.with_name("packages.lock.json").write_text(
                '{"version": 2, "dependencies": {}}\n',
                encoding="utf-8",
            )
            (root / "Directory.Packages.props").write_text(
                "<Project><PropertyGroup>"
                "<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>"
                "</PropertyGroup></Project>\n",
                encoding="utf-8",
            )
            (root / "Axis.sln").write_text(
                'Project("{TYPE}") = "Unexpected", '
                '"src\\Unexpected\\Unexpected.csproj", "{PROJECT}"\n',
                encoding="utf-8",
            )

            issues = axis.dotnet_dependency_lock_issues(root=root)

        self.assertIn("Axis.sln is missing project src/Example/Example.csproj", issues)
        self.assertIn(
            "Axis.sln references unexpected project src/Unexpected/Unexpected.csproj",
            issues,
        )

    def test_ci_uses_global_json_and_locked_nuget_graph(self) -> None:
        for relative in (
            ".github/workflows/build-and-test.yml",
            ".github/workflows/dependency-security.yml",
        ):
            with self.subTest(workflow=relative):
                workflow = (ROOT / relative).read_text(encoding="utf-8")
                self.assertIn("global-json-file: global.json", workflow)
                self.assertIn("cache-dependency-path: '**/packages.lock.json'", workflow)
                self.assertIn("python scripts/axis.py dotnet restore -- --locked-mode", workflow)

    def test_ci_runs_backend_and_frontend_jobs_for_script_changes(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build-and-test.yml").read_text(
            encoding="utf-8"
        )
        filters = workflow.split("          filters: |\n", maxsplit=1)[1].split(
            "\n\n  dotnet:", maxsplit=1
        )[0]
        backend, frontend = filters.split("            frontend:\n", maxsplit=1)

        self.assertIn("- 'scripts/**'", backend)
        self.assertIn("- 'scripts/**'", frontend)

    def test_ci_pr_guard_validates_branch_pr_and_commit_metadata(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build-and-test.yml").read_text(
            encoding="utf-8"
        )
        pr_job = workflow.split("  pr:\n", maxsplit=1)[1].split("\n  detect:\n", maxsplit=1)[0]

        self.assertIn("fetch-depth: 0", pr_job)
        self.assertEqual(2, pr_job.count("uses: phuongnse/engineering-process@"))
        self.assertNotIn("scripts/install_process_runtime.py", pr_job)
        self.assertNotIn(
            "python -m pip install --require-hashes -r requirements/process.txt",
            pr_job,
        )
        self.assertIn("processctl publication validate-pr", pr_job)
        self.assertIn('jq -r \'.pull_request.body // ""\' "$GITHUB_EVENT_PATH"', pr_job)
        self.assertIn(
            'processctl publication validate-range --project-root . --branch "$PR_HEAD_REF" --range "$PR_BASE_SHA..$PR_HEAD_SHA"',
            pr_job,
        )
        self.assertIn("PR_BASE_SHA: ${{ github.event.pull_request.base.sha }}", pr_job)
        self.assertIn("PR_HEAD_SHA: ${{ github.event.pull_request.head.sha }}", pr_job)
        self.assertIn("if: runner.os == 'Linux'", pr_job)
        self.assertIn("Validate process distribution sync", pr_job)
        self.assertIn("Validate Linux process environment", pr_job)
        self.assertIn("Run Linux finite process profiles", pr_job)
        self.assertIn("shared action behavior", pr_job)
        dependency_security = (
            ROOT / ".github" / "workflows" / "dependency-security.yml"
        ).read_text(encoding="utf-8")
        self.assertIn("uses: phuongnse/engineering-process@", dependency_security)
        self.assertNotIn("scripts/install_process_runtime.py", dependency_security)
        self.assertNotIn(
            "python -m pip install --require-hashes -r requirements/process.txt",
            dependency_security,
        )

    def test_secret_scan_pins_runtime_and_reports_every_result_class(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build-and-test.yml").read_text(encoding="utf-8")
        secret_scan = workflow.split("  secret-scan:\n", maxsplit=1)[1]

        self.assertIn(
            "uses: trufflesecurity/trufflehog@37b77001d0174ebec2fcca2bd83ff83a6d45a3ab",
            secret_scan,
        )
        self.assertIn("base: ${{ github.event.pull_request.base.sha }}", secret_scan)
        self.assertIn("head: ${{ github.event.pull_request.head.sha }}", secret_scan)
        self.assertIn("version: 3.95.3", secret_scan)
        extra_args = next(line.strip() for line in secret_scan.splitlines() if "extra_args:" in line)
        self.assertEqual(
            "extra_args: --results=verified,unknown,unverified --fail-on-scan-errors",
            extra_args,
        )

    def test_sync_fast_forwards_existing_branch(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            if args == ["branch", "--show-current"]:
                return "main\n"
            return ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "ref_exists", return_value=True),
            mock.patch.object(axis, "git_is_ancestor", side_effect=[False, True]),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.git_sync(axis.argparse.Namespace(branch="feat/rules")))

        self.assertEqual(
            [
                ["branch", "--show-current"],
                [
                    "fetch",
                    "--no-tags",
                    "origin",
                    "refs/heads/feat/rules:refs/remotes/origin/feat/rules",
                ],
                ["switch", "feat/rules"],
                ["merge", "--ff-only", "refs/remotes/origin/feat/rules"],
            ],
            calls,
        )

    def test_sync_refuses_dirty_tree_before_fetch(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=["user-change.txt"]),
            mock.patch.object(axis, "git", return_value="feat/rules\n") as git,
        ):
            with self.assertRaisesRegex(axis.CheckError, "clean working tree"):
                axis.git_sync(axis.argparse.Namespace(branch="feat/rules"))

        git.assert_called_once_with(["branch", "--show-current"])

    def test_sync_refuses_detached_head_before_inspection_or_fetch(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths") as working_tree,
            mock.patch.object(axis, "git", return_value="") as git,
        ):
            with self.assertRaisesRegex(axis.CheckError, "detached HEAD"):
                axis.git_sync(axis.argparse.Namespace(branch="feat/rules"))

        git.assert_called_once_with(["branch", "--show-current"])
        working_tree.assert_not_called()

    def test_sync_allows_local_branch_ahead(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "feat/rules\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "ref_exists", return_value=True),
            mock.patch.object(axis, "git_is_ancestor", side_effect=[True, False]),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.git_sync(axis.argparse.Namespace(branch="feat/rules")))

        self.assertEqual(
            [
                ["branch", "--show-current"],
                [
                    "fetch",
                    "--no-tags",
                    "origin",
                    "refs/heads/feat/rules:refs/remotes/origin/feat/rules",
                ],
                ["merge", "--ff-only", "refs/remotes/origin/feat/rules"],
            ],
            calls,
        )

    def test_sync_refuses_diverged_branch_before_switch(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "main\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "ref_exists", return_value=True),
            mock.patch.object(axis, "git_is_ancestor", side_effect=[False, False]),
            mock.patch.object(axis, "git", side_effect=fake_git),
        ):
            with self.assertRaisesRegex(axis.CheckError, "diverged"):
                axis.git_sync(axis.argparse.Namespace(branch="feat/rules"))

        self.assertEqual(
            [
                ["branch", "--show-current"],
                [
                    "fetch",
                    "--no-tags",
                    "origin",
                    "refs/heads/feat/rules:refs/remotes/origin/feat/rules",
                ]
            ],
            calls,
        )

    def test_sync_creates_missing_tracking_branch(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "main\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "ref_exists", return_value=False),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.git_sync(axis.argparse.Namespace(branch="feat/rules")))

        self.assertEqual(
            [
                ["branch", "--show-current"],
                [
                    "fetch",
                    "--no-tags",
                    "origin",
                    "refs/heads/feat/rules:refs/remotes/origin/feat/rules",
                ],
                [
                    "switch",
                    "--track",
                    "-c",
                    "feat/rules",
                    "refs/remotes/origin/feat/rules",
                ],
            ],
            calls,
        )

    def test_sync_accepts_renovate_branch(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "main\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "ref_exists", return_value=False),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.git_sync(
                    axis.argparse.Namespace(branch="automation/renovate/all-non-major")
                ),
            )

        self.assertIn(
            [
                "fetch",
                "--no-tags",
                "origin",
                "refs/heads/automation/renovate/all-non-major:"
                "refs/remotes/origin/automation/renovate/all-non-major",
            ],
            calls,
        )

    def test_checkpoint_commits_only_staged_changes_by_default(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "feat/rules\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "git_lines", return_value=["wanted.txt"]),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.git_checkpoint(
                    axis.argparse.Namespace(
                        branch="feat/rules",
                        subject="test: checkpoint",
                        body=None,
                        all_changes=False,
                    )
                ),
            )

        self.assertEqual(
            [["branch", "--show-current"], ["commit", "-m", "test: checkpoint"]],
            calls,
        )

    def test_checkpoint_requires_staged_changes_without_all(self) -> None:
        with (
            mock.patch.object(axis, "git_lines", return_value=[]),
            mock.patch.object(axis, "git") as git,
        ):
            with self.assertRaisesRegex(axis.CheckError, "no staged changes"):
                axis.git_checkpoint(
                    axis.argparse.Namespace(
                        branch="feat/rules",
                        subject="test: checkpoint",
                        body=None,
                        all_changes=False,
                    )
                )

        git.assert_not_called()

    def test_checkpoint_all_explicitly_stages_working_tree(self) -> None:
        calls: list[list[str]] = []

        def fake_git(args: list[str], **_kwargs) -> str:
            calls.append(args)
            return "feat/rules\n" if args == ["branch", "--show-current"] else ""

        with (
            mock.patch.object(axis, "working_tree_paths", return_value=["wanted.txt"]),
            mock.patch.object(axis, "git", side_effect=fake_git),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.git_checkpoint(
                    axis.argparse.Namespace(
                        branch="feat/rules",
                        subject="test: checkpoint",
                        body=None,
                        all_changes=True,
                    )
                ),
            )

        self.assertEqual(
            [
                ["branch", "--show-current"],
                ["add", "--all"],
                ["commit", "-m", "test: checkpoint"],
            ],
            calls,
        )

    def test_cli_routes_sync_and_checkpoint_all(self) -> None:
        with (
            mock.patch.object(axis, "git_sync", return_value=0, create=True) as sync,
            mock.patch.object(axis, "git_checkpoint", return_value=0) as checkpoint,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(0, axis.main(["git", "sync", "--branch", "feat/rules"]))
            self.assertEqual(
                0,
                axis.main(
                    [
                        "git",
                        "checkpoint",
                        "--branch",
                        "feat/rules",
                        "--subject",
                        "test: checkpoint",
                        "--all",
                    ]
                ),
            )

        self.assertEqual("feat/rules", sync.call_args.args[0].branch)
        self.assertTrue(checkpoint.call_args.args[0].all_changes)


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
            mock.patch.object(axis, "npm_cli_command", return_value=["npm"]),
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

        self.assertEqual(
            [
                "dotnet",
                "build",
                "Axis.sln",
                "--nologo",
                "--disable-build-servers",
                "--no-restore",
            ],
            calls[0],
        )

    def test_dotnet_restore_accepts_project_target(self) -> None:
        project = "src/Modules/BusinessObjects/Axis.BusinessObjects.Contracts/Axis.BusinessObjects.Contracts.csproj"
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(
                dotnet_command="restore",
                dotnet_args=[project, "--", "--locked-mode"],
            ),
        )

        self.assertEqual(
            ["dotnet", "restore", project, "--locked-mode"],
            calls[0],
        )

    def test_api_contract_generation_builds_only_api_serially(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0)

        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "run_frontend_npm", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(axis, "sync_solution_openapi_digest") as sync_digest,
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.generate_api_contracts())

        self.assertEqual(
            ["dotnet", "build", "src/Axis.Api/Axis.Api.csproj", "--nologo", "-m:1"],
            calls[1],
        )
        sync_digest.assert_called_once_with()

    def test_solution_openapi_digest_sync_updates_only_configured_digest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            openapi = root / "openapi.json"
            settings = root / "appsettings.json"
            openapi.write_bytes(b'{"openapi":"3.0.1"}')
            settings.write_text(
                '{\n  "Solutions": {\n    "AxisOpenApiSha256": "' + "0" * 64 + '"\n  }\n}\n',
                encoding="utf-8",
            )

            axis.sync_solution_openapi_digest(openapi, settings)

            expected = hashlib.sha256(openapi.read_bytes()).hexdigest()
            self.assertIn(expected, settings.read_text(encoding="utf-8"))

    def test_dotnet_build_accepts_project_target(self) -> None:
        project = "src/Modules/Identity/Axis.Identity.Domain/Axis.Identity.Domain.csproj"
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(
                dotnet_command="build",
                dotnet_args=[project, "--", "--no-restore"],
            ),
        )

        self.assertEqual(
            [
                "dotnet",
                "build",
                project,
                "--nologo",
                "--disable-build-servers",
                "--no-restore",
            ],
            calls[0],
        )

    def test_dotnet_format_accepts_project_target(self) -> None:
        project = "tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj"
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(
                dotnet_command="format",
                dotnet_args=[project, "--", "--no-restore"],
                check=True,
            ),
        )

        self.assertEqual(
            [
                "dotnet",
                "format",
                project,
                "--verify-no-changes",
                "--no-restore",
            ],
            calls[0],
        )

    def test_mcp_serve_keeps_wrapper_diagnostics_off_protocol_stdout(self) -> None:
        calls: list[list[str]] = []
        environments: list[dict[str, str]] = []

        def fake_run(command: list[str], **kwargs):
            calls.append(command)
            environments.append(kwargs.get("env", {}))
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.pem"
            root_ca.write_text("test", encoding="utf-8")
            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", root_ca),
                mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "10.0.302")),
                mock.patch.object(axis, "mcp_api_health_ok", return_value=True),
                mock.patch.object(axis, "run", side_effect=fake_run),
                mock.patch.object(axis, "exe", side_effect=lambda name: name),
                contextlib.redirect_stdout(io.StringIO()) as stdout,
                contextlib.redirect_stderr(io.StringIO()),
            ):
                self.assertEqual(
                    0,
                    axis.mcp_command(
                        axis.argparse.Namespace(mcp_command="serve", mcp_args=["--", "--probe"])
                    ),
                )

        self.assertEqual("", stdout.getvalue())
        self.assertEqual(
            ["dotnet", "build", str(axis.MCP_PROJECT), "--nologo"],
            calls[0],
        )
        self.assertEqual(
            [
                "dotnet",
                "run",
                "--project",
                str(axis.MCP_PROJECT),
                "--no-launch-profile",
                "--no-build",
                "--",
                "--probe",
            ],
            calls[1],
        )
        self.assertEqual("read", environments[1]["AXIS_MCP_ACCESS"])

    def test_mcp_serve_starts_local_dev_before_bridge_when_api_is_unhealthy(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="compose output", stderr="")

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.pem"
            root_ca.write_text("test", encoding="utf-8")
            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", root_ca),
                mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "10.0.302")),
                mock.patch.object(axis, "mcp_api_health_ok", side_effect=[False, True]),
                mock.patch.object(axis, "require_docker_compose", return_value=0),
                mock.patch.object(
                    axis,
                    "require_local_dev_certificates",
                    return_value=0,
                ) as certificate_preflight,
                mock.patch.object(axis, "local_dev_up_args", return_value=["docker", "compose", "up"]),
                mock.patch.object(axis, "run", side_effect=fake_run),
                mock.patch.object(axis, "exe", side_effect=lambda name: name),
                contextlib.redirect_stdout(io.StringIO()) as stdout,
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(
                    0,
                    axis.mcp_command(
                        axis.argparse.Namespace(
                            mcp_command="serve",
                            no_build=True,
                            mcp_args=[],
                        )
                    ),
                )

        self.assertEqual(["docker", "compose", "up"], calls[0])
        certificate_preflight.assert_called_once_with("mcp serve")
        self.assertEqual("dotnet", calls[1][0])
        self.assertEqual("", stdout.getvalue())
        self.assertIn("starting the local-dev stack", stderr.getvalue())

    def test_mcp_serve_passes_explicit_write_access_to_bridge(self) -> None:
        environments: list[dict[str, str]] = []

        def fake_run(command: list[str], **kwargs):
            environments.append(kwargs.get("env", {}))
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.pem"
            root_ca.write_text("test", encoding="utf-8")
            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_PEM", root_ca),
                mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "10.0.302")),
                mock.patch.object(axis, "mcp_api_health_ok", return_value=True),
                mock.patch.object(axis, "run", side_effect=fake_run),
                mock.patch.object(axis, "exe", side_effect=lambda name: name),
                contextlib.redirect_stdout(io.StringIO()),
                contextlib.redirect_stderr(io.StringIO()),
            ):
                self.assertEqual(
                    0,
                    axis.mcp_command(
                        axis.argparse.Namespace(
                            mcp_command="serve",
                            access="write",
                            mcp_args=[],
                        )
                    ),
                )

        self.assertEqual("write", environments[1]["AXIS_MCP_ACCESS"])

    def test_dotnet_restore_tools_uses_repository_manifest(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="restore-tools", dotnet_args=[]),
        )

        self.assertEqual(["dotnet", "tool", "restore"], calls[0])

    def test_frontend_sync_lock_regenerates_from_exact_manifest_without_scripts(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(frontend_command="sync-lock"),
        )

        self.assertEqual(["npm", "install", "--package-lock-only", "--ignore-scripts"], calls[0])

    def test_frontend_sync_lock_applies_compatible_audit_fixes_without_force_or_scripts(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(frontend_command="sync-lock", audit_fix=True),
        )

        self.assertEqual(
            ["npm", "audit", "fix", "--package-lock-only", "--ignore-scripts"],
            calls[0],
        )
        self.assertNotIn("--force", calls[0])

    def test_dotnet_build_strips_argparse_separator(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="build", dotnet_args=["--", "--no-restore"]),
        )

        self.assertEqual(
            [
                "dotnet",
                "build",
                "Axis.sln",
                "--nologo",
                "--disable-build-servers",
                "--no-restore",
            ],
            calls[0],
        )

    def test_dotnet_test_uses_solution_by_default(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="test", dotnet_args=["--", "--no-build"]),
        )

        self.assertEqual(
            [
                "dotnet",
                "test",
                "Axis.sln",
                "--nologo",
                "--disable-build-servers",
                "--no-build",
            ],
            calls[0],
        )

    def test_dotnet_test_accepts_project_target(self) -> None:
        project = "tests/Modules/Rules/Axis.Rules.Application.Tests/Axis.Rules.Application.Tests.csproj"
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(
                dotnet_command="test",
                dotnet_args=[project, "--", "--filter", "FullyQualifiedName~RuleAssetDefinitionHandlerTests"],
            ),
        )

        self.assertEqual(
            [
                "dotnet",
                "test",
                project,
                "--nologo",
                "--disable-build-servers",
                "--filter",
                "FullyQualifiedName~RuleAssetDefinitionHandlerTests",
            ],
            calls[0],
        )

    def test_dotnet_format_check_uses_verify_no_changes(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="format", check=True, dotnet_args=[]),
        )

        self.assertEqual(["dotnet", "format", "Axis.sln", "--verify-no-changes"], calls[0])

    def test_dotnet_format_disables_msbuild_node_reuse(self) -> None:
        completed = axis.subprocess.CompletedProcess([], 0)
        args = axis.argparse.Namespace(
            dotnet_command="format",
            check=True,
            dotnet_args=[],
        )

        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "exe", return_value="dotnet"),
            mock.patch.object(axis, "run", return_value=completed) as run,
        ):
            self.assertEqual(0, axis.dotnet_command(args))

        run.assert_called_once_with(
            ["dotnet", "format", "Axis.sln", "--verify-no-changes"],
            check=False,
            env={"MSBUILDDISABLENODEREUSE": "1"},
        )

    def test_migration_add_uses_owned_module_contracts(self) -> None:
        targets = {
            "audit": (
                "Audit/Axis.Audit.Infrastructure/"
                "Axis.Audit.Infrastructure.csproj",
                "AuditDbContext",
            ),
            "authorization": (
                "Authorization/Axis.Authorization.Infrastructure/"
                "Axis.Authorization.Infrastructure.csproj",
                "AuthorizationDbContext",
            ),
            "business-objects": (
                "BusinessObjects/Axis.BusinessObjects.Infrastructure/"
                "Axis.BusinessObjects.Infrastructure.csproj",
                "BusinessObjectsDbContext",
            ),
            "identity": (
                "Identity/Axis.Identity.Infrastructure/"
                "Axis.Identity.Infrastructure.csproj",
                "IdentityDbContext",
            ),
            "rules": (
                "Rules/Axis.Rules.Infrastructure/"
                "Axis.Rules.Infrastructure.csproj",
                "RulesDbContext",
            ),
            "solutions": (
                "Solutions/Axis.Solutions.Infrastructure/"
                "Axis.Solutions.Infrastructure.csproj",
                "SolutionsDbContext",
            ),
        }
        for module, (project_suffix, context) in targets.items():
            with self.subTest(module=module):
                calls = self.run_with_fake_process(
                    axis.migration_command,
                    axis.argparse.Namespace(
                        migration_command="add",
                        module=module,
                        name="AddDecisionTables",
                    ),
                )

                project = str(axis.ROOT / "src" / "Modules" / project_suffix)
                self.assertEqual(
                    [
                        "dotnet",
                        "tool",
                        "restore",
                        "--tool-manifest",
                        str(axis.ROOT / "dotnet-tools.json"),
                    ],
                    calls[0],
                )
                self.assertEqual(
                    [
                        "dotnet",
                        "build",
                        project,
                        "--nologo",
                        "-m:1",
                    ],
                    calls[1],
                )
                self.assertEqual(
                    [
                        "dotnet",
                        "ef",
                        "migrations",
                        "add",
                        "AddDecisionTables",
                        "--project",
                        project,
                        "--startup-project",
                        project,
                        "--context",
                        context,
                        "--output-dir",
                        "Migrations",
                        "--no-build",
                    ],
                    calls[2],
                )

    def test_cli_routes_finite_migration_add(self) -> None:
        with (
            mock.patch.object(axis, "migration_command", return_value=0) as migration,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.main(["migration", "add", "rules", "AddDecisionTables"]),
            )

        args = migration.call_args.args[0]
        self.assertEqual("rules", args.module)
        self.assertEqual("AddDecisionTables", args.name)

    def test_migration_remove_uses_owned_module_contract(self) -> None:
        calls = self.run_with_fake_process(
            axis.migration_command,
            axis.argparse.Namespace(
                migration_command="remove",
                module="business-objects",
            ),
        )

        project = str(
            axis.ROOT
            / "src"
            / "Modules"
            / "BusinessObjects"
            / "Axis.BusinessObjects.Infrastructure"
            / "Axis.BusinessObjects.Infrastructure.csproj"
        )
        self.assertEqual(
            [
                "dotnet",
                "tool",
                "restore",
                "--tool-manifest",
                str(axis.ROOT / "dotnet-tools.json"),
            ],
            calls[0],
        )
        self.assertEqual(
            ["dotnet", "build", project, "--nologo", "-m:1"],
            calls[1],
        )
        self.assertEqual(
            [
                "dotnet",
                "ef",
                "migrations",
                "remove",
                "--project",
                project,
                "--startup-project",
                project,
                "--context",
                "BusinessObjectsDbContext",
                "--force",
                "--no-build",
            ],
            calls[2],
        )

    def test_migration_stops_when_repo_tool_restore_fails(self) -> None:
        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(
                axis,
                "run",
                side_effect=[axis.subprocess.CompletedProcess([], 23)],
            ) as run,
        ):
            self.assertEqual(
                23,
                axis.migration_command(
                    axis.argparse.Namespace(
                        migration_command="add",
                        module="rules",
                        name="AddDecisionTables",
                    )
                ),
            )

        run.assert_called_once_with(
            [
                axis.exe("dotnet"),
                "tool",
                "restore",
                "--tool-manifest",
                str(axis.ROOT / "dotnet-tools.json"),
            ],
            check=False,
        )

    def test_cli_routes_finite_migration_remove(self) -> None:
        with (
            mock.patch.object(axis, "migration_command", return_value=0) as migration,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(
                0,
                axis.main(["migration", "remove", "business-objects"]),
            )

        args = migration.call_args.args[0]
        self.assertEqual("remove", args.migration_command)
        self.assertEqual("business-objects", args.module)

    def test_migration_add_uses_non_routable_design_time_connection(self) -> None:
        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(
                axis,
                "run",
                return_value=axis.subprocess.CompletedProcess([], 0),
            ) as run,
            mock.patch.object(axis, "normalize_migration_source_encoding") as normalize,
        ):
            self.assertEqual(
                0,
                axis.migration_command(
                    axis.argparse.Namespace(
                        migration_command="add",
                        module="identity",
                        name="AddPreference",
                    )
                ),
            )

        self.assertEqual(
            {
                "ConnectionStrings__Identity": axis.DESIGN_TIME_CONNECTION_STRING,
            },
            run.call_args.kwargs["env"],
        )
        normalize.assert_called_once_with(
            axis.ROOT
            / "src"
            / "Modules"
            / "Identity"
            / "Axis.Identity.Infrastructure"
            / "Migrations"
        )

    def test_migration_add_rejects_invalid_name_before_running_dotnet(self) -> None:
        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(axis, "run") as run,
        ):
            with self.assertRaisesRegex(axis.CheckError, "PascalCase"):
                axis.migration_command(
                    axis.argparse.Namespace(
                        migration_command="add",
                        module="rules",
                        name="../unsafe",
                    )
                )

        run.assert_not_called()

    def test_migration_workflow_normalizes_generated_utf8_bom(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            migrations = Path(temporary)
            generated = migrations / "20260817000000_Initial.cs"
            generated.write_bytes(b"\xef\xbb\xbfusing System;\n")
            unchanged = migrations / "Snapshot.cs"
            unchanged.write_bytes(b"using System;\n")

            axis.normalize_migration_source_encoding(migrations)

            self.assertEqual(b"using System;\n", generated.read_bytes())
            self.assertEqual(b"using System;\n", unchanged.read_bytes())

    def test_frontend_test_maps_paths_and_name_filter(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(
                frontend_command="test",
                test_paths=["tests/rules-page.test.tsx"],
                name="publishes",
            ),
        )

        self.assertEqual(
            [
                "npm",
                "run",
                "test",
                "--",
                "tests/rules-page.test.tsx",
                "-t",
                "publishes",
            ],
            calls[0],
        )

    def test_frontend_test_rejects_arbitrary_vitest_flags(self) -> None:
        with (
            self.assertRaises(SystemExit),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            axis.main(["frontend", "test", "--watch"])

    def test_frontend_test_related_maps_checkpoint_to_bounded_dependency_graph(self) -> None:
        with mock.patch.object(
            axis,
            "changed_paths_since",
            return_value=[
                "frontend/src/main.tsx",
                "frontend/tests/app-shell.test.tsx",
                "src/Axis.Api/Program.cs",
            ],
        ):
            calls = self.run_with_fake_process(
                axis.frontend_command,
                axis.argparse.Namespace(
                    frontend_command="test-related",
                    since="reviewed-checkpoint",
                ),
            )

        self.assertEqual(
            [
                "npm",
                "exec",
                "--",
                "vitest",
                "related",
                "src/main.tsx",
                "tests/app-shell.test.tsx",
                "--run",
            ],
            calls[0],
        )

    def test_frontend_format_maps_only_selected_source_paths(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(
                frontend_command="format",
                source_paths=["src/features/solutions/page.tsx", "src/index.css"],
            ),
        )

        self.assertEqual(
            [
                "npm",
                "exec",
                "--",
                "biome",
                "check",
                "--write",
                "src/features/solutions/page.tsx",
                "src/index.css",
            ],
            calls[0],
        )

    def test_frontend_format_rejects_flags_and_non_source_paths(self) -> None:
        with (
            self.assertRaises(SystemExit),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            axis.main(["frontend", "format", "--write"])

        with tempfile.TemporaryDirectory() as temp:
            frontend = Path(temp)
            (frontend / "README.md").write_text("not source", encoding="utf-8")
            with (
                mock.patch.object(axis, "FRONTEND_DIR", frontend),
                self.assertRaises(SystemExit),
                contextlib.redirect_stderr(io.StringIO()),
            ):
                axis.main(["frontend", "format", "README.md"])

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
            generated = frontend / "src" / "lib" / "api-generated" / "types.gen.ts"
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

    def test_frontend_gen_api_types_check_restores_files_when_generator_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            frontend = Path(temp)
            generated = frontend / "src" / "lib" / "api-generated" / "types.gen.ts"
            generated.parent.mkdir(parents=True)
            generated.write_text("current", encoding="utf-8")

            def generate(_args: list[str]) -> subprocess.CompletedProcess[str]:
                generated.write_text("partial", encoding="utf-8")
                (generated.parent / "partial.gen.ts").write_text("partial", encoding="utf-8")
                return subprocess.CompletedProcess([], 7)

            with (
                mock.patch.object(axis, "FRONTEND_DIR", frontend),
                mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
                mock.patch.object(axis, "run_frontend_npm", side_effect=generate),
            ):
                rc = axis.frontend_command(
                    axis.argparse.Namespace(frontend_command="gen-api-types", check=True)
                )

            self.assertEqual(7, rc)
            self.assertEqual("current", generated.read_text(encoding="utf-8"))
            self.assertFalse((generated.parent / "partial.gen.ts").exists())

    def test_frontend_command_runs_npm_with_resolved_frontend_env(self) -> None:
        calls: list[dict[str, dict[str, str] | None]] = []
        frontend_env = {"PATH": "/tmp/nvm-node-bin:/usr/bin"}

        def fake_run(command: list[str], **kwargs):
            calls.append({"env": kwargs.get("env")})
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
            mock.patch.object(axis, "check_frontend_toolchain", return_value=0),
            mock.patch.object(axis, "frontend_toolchain_env", return_value=frontend_env),
            mock.patch.object(
                axis,
                "npm_cli_command",
                return_value=["node", "npm-cli.js"],
            ) as npm_cli_command,
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.frontend_command(axis.argparse.Namespace(frontend_command="ci")))

        npm_cli_command.assert_called_once_with(env=frontend_env)
        self.assertEqual(frontend_env, calls[0]["env"])

    def test_generate_ui_baseline_uses_deterministic_python_generator(self) -> None:
        with mock.patch.object(axis, "write_ui_baseline") as write_baseline:
            rc = axis.generate_ui_baseline()

        self.assertEqual(0, rc)
        write_baseline.assert_called_once_with()

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
                mock.patch.object(
                    axis,
                    "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                    cert_dir / "trusted-rootCA.sha1",
                ),
                mock.patch.object(axis, "run", side_effect=fake_run),
                mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
                mock.patch.object(axis.os, "name", "posix"),
                mock.patch.object(axis.Path, "chmod", autospec=True) as chmod,
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.local_dev_certs())

            self.assertTrue((cert_dir / "localhost.ext").is_file())
            leaf_extensions = (cert_dir / "localhost.ext").read_text(encoding="utf-8")
            self.assertIn("basicConstraints=CA:FALSE", leaf_extensions)
            self.assertIn("keyUsage=digitalSignature,keyEncipherment", leaf_extensions)
            self.assertIn("extendedKeyUsage=serverAuth", leaf_extensions)
            self.assertIn("subjectAltName=@alt_names", leaf_extensions)
            chmod.assert_any_call(cert_dir, 0o700)
            chmod.assert_any_call(cert_dir / "rootCA-key.pem", 0o600)
            chmod.assert_any_call(cert_dir / "localhost-key.pem", 0o600)
            self.assertEqual("/usr/bin/openssl", calls[0][0])
            root_ca_command = next(
                command
                for command in calls
                if str(cert_dir / "rootCA.pem") in command
            )
            self.assertIn("basicConstraints=critical,CA:TRUE", root_ca_command)
            self.assertIn("keyUsage=critical,keyCertSign,cRLSign", root_ca_command)

    def test_local_dev_certificate_reuse_requires_server_leaf_extensions(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            cert_dir = Path(temp)
            paths = {
                "LOCAL_ROOT_CA_KEY": cert_dir / "rootCA-key.pem",
                "LOCAL_ROOT_CA_PEM": cert_dir / "rootCA.pem",
                "LOCAL_ROOT_CA_CER": cert_dir / "rootCA.cer",
                "LOCALHOST_KEY": cert_dir / "localhost-key.pem",
                "LOCALHOST_CERT": cert_dir / "localhost.pem",
            }
            for path in paths.values():
                path.write_text("certificate material\n", encoding="utf-8")

            valid_server_usage = True

            def fake_run(command: list[str], **_kwargs):
                if "-ext" in command:
                    extension = command[command.index("-ext") + 1]
                    is_leaf = str(paths["LOCALHOST_CERT"]) in command
                    if is_leaf and extension == "basicConstraints":
                        stdout = "X509v3 Basic Constraints:\n    CA:FALSE\n"
                    elif is_leaf and extension == "keyUsage":
                        stdout = "X509v3 Key Usage:\n    Digital Signature, Key Encipherment\n"
                    elif is_leaf and extension == "extendedKeyUsage":
                        usage = "TLS Web Server Authentication" if valid_server_usage else "Code Signing"
                        stdout = f"X509v3 Extended Key Usage:\n    {usage}\n"
                    elif extension == "basicConstraints":
                        stdout = "X509v3 Basic Constraints: critical\n    CA:TRUE\n"
                    else:
                        stdout = "X509v3 Key Usage: critical\n    Certificate Sign, CRL Sign\n"
                    return axis.subprocess.CompletedProcess(command, 0, stdout=stdout, stderr="")
                if "-fingerprint" in command:
                    return axis.subprocess.CompletedProcess(
                        command,
                        0,
                        stdout="sha256 Fingerprint=ROOT\n",
                        stderr="",
                    )
                return axis.subprocess.CompletedProcess(command, 0, stdout="public-key\n", stderr="")

            with contextlib.ExitStack() as stack:
                for name, path in paths.items():
                    stack.enter_context(mock.patch.object(axis, name, path))
                stack.enter_context(mock.patch.object(axis, "run", side_effect=fake_run))

                self.assertTrue(axis.local_dev_certificates_valid("openssl"))
                valid_server_usage = False
                self.assertFalse(axis.local_dev_certificates_valid("openssl"))

    def test_local_dev_certs_reuses_current_and_regenerates_legacy_material(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            cert_dir = Path(temp) / ".dev-certs"
            cert_dir.mkdir()
            paths = {
                "LOCAL_CERT_DIR": cert_dir,
                "LOCAL_ROOT_CA_KEY": cert_dir / "rootCA-key.pem",
                "LOCAL_ROOT_CA_PEM": cert_dir / "rootCA.pem",
                "LOCAL_ROOT_CA_CER": cert_dir / "rootCA.cer",
                "LOCALHOST_KEY": cert_dir / "localhost-key.pem",
                "LOCALHOST_CSR": cert_dir / "localhost.csr",
                "LOCALHOST_EXT": cert_dir / "localhost.ext",
                "LOCALHOST_CERT": cert_dir / "localhost.pem",
            }
            for path in paths.values():
                if path != cert_dir:
                    path.write_text("existing\n", encoding="utf-8")

            calls: list[list[str]] = []
            legacy_key_usage = False
            legacy_der_key_usage = False
            legacy_der_identity = False

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
                if "-ext" in command:
                    is_leaf = str(paths["LOCALHOST_CERT"]) in command
                    if is_leaf and "basicConstraints" in command:
                        stdout = "X509v3 Basic Constraints:\n    CA:FALSE\n"
                    elif is_leaf and "extendedKeyUsage" in command:
                        stdout = "X509v3 Extended Key Usage:\n    TLS Web Server Authentication\n"
                    elif is_leaf:
                        stdout = "X509v3 Key Usage:\n    Digital Signature, Key Encipherment\n"
                    elif "basicConstraints" in command:
                        stdout = "X509v3 Basic Constraints: critical\n    CA:TRUE\n"
                    elif legacy_key_usage or (legacy_der_key_usage and "-inform" in command):
                        stdout = "X509v3 Key Usage: critical\n    Certificate Sign\n"
                    else:
                        stdout = "X509v3 Key Usage: critical\n    Certificate Sign, CRL Sign\n"
                    return axis.subprocess.CompletedProcess(command, 0, stdout=stdout, stderr="")
                if "-fingerprint" in command:
                    fingerprint = "DER" if legacy_der_identity and "-inform" in command else "ROOT"
                    return axis.subprocess.CompletedProcess(
                        command,
                        0,
                        stdout=f"sha256 Fingerprint={fingerprint}\n",
                        stderr="",
                    )
                return axis.subprocess.CompletedProcess(command, 0, stdout="public-key\n", stderr="")

            patches = [mock.patch.object(axis, name, value) for name, value in paths.items()]
            with contextlib.ExitStack() as stack:
                for patcher in patches:
                    stack.enter_context(patcher)
                stack.enter_context(mock.patch.object(axis, "run", side_effect=fake_run))
                stack.enter_context(mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"))
                stack.enter_context(mock.patch.object(axis.os, "name", "posix"))
                stack.enter_context(mock.patch.object(axis.Path, "chmod", autospec=True))
                stack.enter_context(
                    mock.patch.object(
                        axis,
                        "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                        cert_dir / "trusted-rootCA.sha1",
                    )
                )
                stdout = stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
                self.assertEqual(0, axis.local_dev_certs(axis.argparse.Namespace(renew=False)))

                self.assertTrue(any("-ext" in command and "keyUsage" in command for command in calls))
                self.assertTrue(any("-inform" in command and "-ext" in command for command in calls))
                self.assertTrue(any("-fingerprint" in command for command in calls))
                self.assertFalse(any("genrsa" in command for command in calls))
                self.assertIn("reusing", stdout.getvalue())

                legacy_key_usage = True
                calls.clear()
                stdout.seek(0)
                stdout.truncate(0)
                self.assertEqual(0, axis.local_dev_certs(axis.argparse.Namespace(renew=False)))

            self.assertTrue(any("-checkend" in command for command in calls))
            self.assertTrue(any("-checkhost" in command and "api" in command for command in calls))
            self.assertTrue(any("-checkip" in command and "::1" in command for command in calls))
            self.assertTrue(any("genrsa" in command for command in calls))
            self.assertIn("generated", stdout.getvalue())

            legacy_key_usage = False
            legacy_der_key_usage = True
            calls.clear()
            stdout.seek(0)
            stdout.truncate(0)
            with contextlib.ExitStack() as stack:
                for patcher in patches:
                    stack.enter_context(patcher)
                stack.enter_context(mock.patch.object(axis, "run", side_effect=fake_run))
                stack.enter_context(mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"))
                stack.enter_context(mock.patch.object(axis.os, "name", "posix"))
                stack.enter_context(mock.patch.object(axis.Path, "chmod", autospec=True))
                stack.enter_context(
                    mock.patch.object(
                        axis,
                        "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                        cert_dir / "trusted-rootCA.sha1",
                    )
                )
                with contextlib.redirect_stdout(stdout):
                    self.assertEqual(0, axis.local_dev_certs(axis.argparse.Namespace(renew=False)))

            self.assertTrue(any("-inform" in command and "-ext" in command for command in calls))
            self.assertTrue(any("genrsa" in command for command in calls))

            legacy_der_key_usage = False
            legacy_der_identity = True
            calls.clear()
            stdout.seek(0)
            stdout.truncate(0)
            with contextlib.ExitStack() as stack:
                for patcher in patches:
                    stack.enter_context(patcher)
                stack.enter_context(mock.patch.object(axis, "run", side_effect=fake_run))
                stack.enter_context(mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"))
                stack.enter_context(mock.patch.object(axis.os, "name", "posix"))
                stack.enter_context(mock.patch.object(axis.Path, "chmod", autospec=True))
                stack.enter_context(
                    mock.patch.object(
                        axis,
                        "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                        cert_dir / "trusted-rootCA.sha1",
                    )
                )
                with contextlib.redirect_stdout(stdout):
                    self.assertEqual(0, axis.local_dev_certs(axis.argparse.Namespace(renew=False)))

            self.assertTrue(any("-fingerprint" in command for command in calls))
            self.assertTrue(any("genrsa" in command for command in calls))

    def test_local_dev_certs_refuses_to_replace_an_axis_managed_trusted_ca(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            cert_dir = Path(temp) / ".dev-certs"
            cert_dir.mkdir()
            root_ca = cert_dir / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            trusted_marker = cert_dir / "trusted-rootCA.sha1"
            trusted_marker.write_text(
                f"{hashlib.sha1(root_ca.read_bytes()).hexdigest().upper()}\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(axis, "LOCAL_CERT_DIR", cert_dir),
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(axis, "LOCALHOST_EXT", cert_dir / "localhost.ext"),
                mock.patch.object(
                    axis,
                    "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                    trusted_marker,
                    create=True,
                ),
                mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
                mock.patch.object(axis, "run_required") as run_required,
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.local_dev_certs(axis.argparse.Namespace(renew=True)))

            run_required.assert_not_called()
            self.assertIn("untrust-certs", stderr.getvalue())

    def test_trust_certs_imports_root_ca_into_windows_user_store_from_wsl(self) -> None:
        handler = getattr(axis, "local_dev_trust_certs", None)
        self.assertTrue(callable(handler))

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            trusted_marker = Path(temp) / "trusted-rootCA.sha1"
            calls: list[list[str]] = []
            call_options: list[dict[str, object]] = []

            def fake_run(command: list[str], **kwargs):
                calls.append(command)
                call_options.append(kwargs)
                stdout = "C:\\axis\\rootCA.cer\n" if command[0] == "wslpath" else ""
                return axis.subprocess.CompletedProcess(command, 0, stdout=stdout, stderr="")

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(
                    axis,
                    "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                    trusted_marker,
                    create=True,
                ),
                mock.patch.object(axis, "local_dev_cert_host", return_value="wsl"),
                mock.patch.object(axis.shutil, "which", side_effect=lambda name: name),
                mock.patch.object(axis, "run", side_effect=fake_run),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, handler(axis.argparse.Namespace(yes=True)))

            self.assertEqual(["wslpath", "-w", str(root_ca)], calls[0])
            self.assertEqual(
                ["certutil.exe", "-f", "-user", "-addstore", "Root", "C:\\axis\\rootCA.cer"],
                calls[1],
            )
            self.assertTrue(call_options[1].get("capture"))
            self.assertTrue(trusted_marker.is_file())
            self.assertEqual(
                f"{hashlib.sha1(b'axis-root-ca').hexdigest().upper()}\n",
                trusted_marker.read_text(encoding="utf-8"),
            )

    def test_host_trust_status_checks_the_windows_current_user_root_store(self) -> None:
        handler = getattr(axis, "local_dev_host_trust_status", None)
        self.assertTrue(callable(handler))

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            calls: list[list[str]] = []

            def fake_run_optional(command: list[str], **_kwargs):
                calls.append(command)
                return axis.subprocess.CompletedProcess(command, 0, stdout="certificate", stderr="")

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(axis, "local_dev_cert_host", return_value="wsl"),
                mock.patch.object(axis.shutil, "which", return_value="certutil.exe"),
                mock.patch.object(axis, "run_optional", side_effect=fake_run_optional),
            ):
                status, detail = handler()

        self.assertEqual("OK", status)
        self.assertIn("trusted", detail)
        self.assertEqual(
            [
                "certutil.exe",
                "-user",
                "-store",
                "Root",
                hashlib.sha1(b"axis-root-ca").hexdigest().upper(),
            ],
            calls[0],
        )

    def test_host_trust_status_warns_when_windows_store_lacks_the_ca(self) -> None:
        handler = getattr(axis, "local_dev_host_trust_status", None)
        self.assertTrue(callable(handler))

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(axis, "local_dev_cert_host", return_value="wsl"),
                mock.patch.object(axis.shutil, "which", return_value="certutil.exe"),
                mock.patch.object(
                    axis,
                    "run_optional",
                    return_value=axis.subprocess.CompletedProcess([], 1, stdout="", stderr="not found"),
                ),
            ):
                status, detail = handler()

        self.assertEqual("WARN", status)
        self.assertIn("local-dev trust-certs", detail)

    def test_untrust_certs_removes_root_ca_from_windows_user_store(self) -> None:
        handler = getattr(axis, "local_dev_untrust_certs", None)
        self.assertTrue(callable(handler))

        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            trusted_marker = Path(temp) / "trusted-rootCA.sha1"
            managed_fingerprint = hashlib.sha1(b"previous-axis-root-ca").hexdigest().upper()
            trusted_marker.write_text(f"{managed_fingerprint}\n", encoding="utf-8")
            calls: list[list[str]] = []

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
                return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(
                    axis,
                    "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT",
                    trusted_marker,
                    create=True,
                ),
                mock.patch.object(axis, "local_dev_cert_host", return_value="wsl"),
                mock.patch.object(axis.shutil, "which", side_effect=lambda name: name),
                mock.patch.object(axis, "run", side_effect=fake_run),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, handler(axis.argparse.Namespace(yes=True)))

            self.assertEqual(
                ["certutil.exe", "-user", "-delstore", "Root", managed_fingerprint],
                calls[0],
            )
            self.assertFalse(trusted_marker.exists())

    def test_untrust_certs_uses_managed_fingerprint_when_root_ca_is_missing(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "missing-rootCA.cer"
            trusted_marker = Path(temp) / "trusted-rootCA.sha1"
            managed_fingerprint = hashlib.sha1(b"missing-axis-root-ca").hexdigest().upper()
            trusted_marker.write_text(f"{managed_fingerprint}\n", encoding="utf-8")
            calls: list[list[str]] = []

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
                return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(axis, "LOCAL_TRUSTED_ROOT_CA_FINGERPRINT", trusted_marker),
                mock.patch.object(axis, "local_dev_cert_host", return_value="wsl"),
                mock.patch.object(axis.shutil, "which", side_effect=lambda name: name),
                mock.patch.object(axis, "run", side_effect=fake_run),
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.local_dev_untrust_certs(axis.argparse.Namespace(yes=True)))

            self.assertEqual(
                ["certutil.exe", "-user", "-delstore", "Root", managed_fingerprint],
                calls[0],
            )
            self.assertFalse(trusted_marker.exists())

    def test_native_linux_trust_prints_manual_guidance_without_running_a_command(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root_ca = Path(temp) / "rootCA.cer"
            root_ca.write_bytes(b"axis-root-ca")
            with (
                mock.patch.object(axis, "LOCAL_ROOT_CA_CER", root_ca),
                mock.patch.object(axis, "local_dev_cert_host", return_value="linux"),
                mock.patch.object(axis, "run") as run,
                contextlib.redirect_stdout(io.StringIO()) as stdout,
                contextlib.redirect_stderr(io.StringIO()) as stderr,
            ):
                self.assertEqual(1, axis.local_dev_trust_certs(axis.argparse.Namespace(yes=False)))

        run.assert_not_called()
        self.assertIn("SHA-256", stdout.getvalue())
        self.assertIn("browser or user trust store", stderr.getvalue())

    def test_local_dev_cli_routes_certificate_lifecycle_flags(self) -> None:
        with (
            mock.patch.object(axis, "local_dev_certs", return_value=0) as certs,
            mock.patch.object(axis, "local_dev_trust_certs", return_value=0) as trust,
            mock.patch.object(axis, "local_dev_host_smoke", return_value=0, create=True) as host_smoke,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.main(["local-dev", "certs", "--renew"]))
            self.assertEqual(0, axis.main(["local-dev", "trust-certs", "--yes"]))
            self.assertEqual(0, axis.main(["local-dev", "host-smoke"]))

        self.assertTrue(certs.call_args.args[0].renew)
        self.assertTrue(trust.call_args.args[0].yes)
        host_smoke.assert_called_once()


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

    def test_refuses_an_explicitly_empty_core_hooks_path_without_writing(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            target = temp_root / ".git" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("axis hook\n", encoding="utf-8")

            def fake_run(args: list[str], **_kwargs):
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout="\n", stderr="")
                if args[1:] == ["rev-parse", "--git-common-dir"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout=f"{temp_root / '.git'}\n", stderr=""
                    )
                raise AssertionError(f"unexpected command: {args}")

            original_root = axis.ROOT
            axis.ROOT = temp_root
            try:
                with (
                    mock.patch.object(axis, "run", side_effect=fake_run),
                    mock.patch.object(axis, "exe", side_effect=lambda name: name),
                    contextlib.redirect_stderr(io.StringIO()) as stderr,
                ):
                    self.assertEqual(1, axis.install_hooks())
            finally:
                axis.ROOT = original_root

            self.assertFalse(target.exists())
            self.assertIn("empty core.hooksPath", stderr.getvalue())

    def test_refuses_inherited_repo_hooks_path_that_resolves_to_tracked_source(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("axis hook\n", encoding="utf-8")

            def fake_run(args: list[str], **_kwargs):
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout="scripts/hooks\n", stderr=""
                    )
                if args[1:] == ["config", "--local", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(args, 1, stdout="", stderr="")
                raise AssertionError(f"unexpected command: {args}")

            original_root = axis.ROOT
            axis.ROOT = temp_root
            try:
                with (
                    mock.patch.object(axis, "run", side_effect=fake_run),
                    mock.patch.object(axis, "exe", side_effect=lambda name: name),
                    contextlib.redirect_stderr(io.StringIO()) as stderr,
                ):
                    self.assertEqual(1, axis.install_hooks())
            finally:
                axis.ROOT = original_root

            self.assertEqual("axis hook\n", source.read_text(encoding="utf-8"))
            self.assertFalse(source.with_name("pre-push.axis-managed.sha256").exists())
            self.assertIn("inherited core.hooksPath", stderr.getvalue())

    def test_refuses_local_migration_when_an_inherited_hooks_path_exists_without_mutation(self) -> None:
        local_hooks_configured = True
        calls: list[list[str]] = []

        def fake_run(args: list[str], **_kwargs):
            nonlocal local_hooks_configured
            calls.append(args)
            if args[1:] == ["config", "--get", "core.hooksPath"]:
                value = "scripts/hooks\n" if local_hooks_configured else "global/hooks\n"
                return axis.subprocess.CompletedProcess(args, 0, stdout=value, stderr="")
            if args[1:] == ["config", "--local", "--get", "core.hooksPath"]:
                return axis.subprocess.CompletedProcess(
                    args, 0, stdout="scripts/hooks\n", stderr=""
                )
            if args[1:] == ["config", "--show-scope", "--get-all", "core.hooksPath"]:
                return axis.subprocess.CompletedProcess(
                    args,
                    0,
                    stdout="global\tglobal/hooks\nlocal\tscripts/hooks\n",
                    stderr="",
                )
            if args[1:] == ["config", "--local", "--unset-all", "core.hooksPath"]:
                local_hooks_configured = False
                return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")
            raise AssertionError(f"unexpected command: {args}")

        with (
            mock.patch.object(axis, "run", side_effect=fake_run),
            mock.patch.object(axis, "exe", side_effect=lambda name: name),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.install_hooks())

        self.assertTrue(local_hooks_configured)
        self.assertNotIn(
            ["git", "config", "--local", "--unset-all", "core.hooksPath"],
            calls,
        )
        self.assertIn("inherited core.hooksPath", stderr.getvalue())

    def test_restores_local_hooks_path_when_post_unset_validation_detects_a_race(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("axis hook\n", encoding="utf-8")
            local_hooks_configured = True

            def fake_run(args: list[str], **_kwargs):
                nonlocal local_hooks_configured
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    value = "scripts/hooks\n" if local_hooks_configured else "raced/hooks\n"
                    return axis.subprocess.CompletedProcess(args, 0, stdout=value, stderr="")
                if args[1:] == ["config", "--local", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout="scripts/hooks\n", stderr=""
                    )
                if args[1:] == ["config", "--show-scope", "--get-all", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout="local\tscripts/hooks\n", stderr=""
                    )
                if args[1:] == ["config", "--local", "--unset-all", "core.hooksPath"]:
                    local_hooks_configured = False
                    return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")
                if args[1:] == ["config", "--local", "core.hooksPath", "scripts/hooks"]:
                    local_hooks_configured = True
                    return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")
                if args[1:] == ["rev-parse", "--git-common-dir"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout=f"{temp_root / '.git'}\n", stderr=""
                    )
                raise AssertionError(f"unexpected command: {args}")

            original_root = axis.ROOT
            axis.ROOT = temp_root
            try:
                with (
                    mock.patch.object(axis, "run", side_effect=fake_run),
                    mock.patch.object(axis, "exe", side_effect=lambda name: name),
                    contextlib.redirect_stderr(io.StringIO()) as stderr,
                ):
                    self.assertEqual(1, axis.install_hooks())
            finally:
                axis.ROOT = original_root

            self.assertTrue(local_hooks_configured)
            self.assertIn("original local value was restored", stderr.getvalue())

    def test_derives_hook_target_only_from_the_common_git_directory(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            outside = temp_root / "outside" / "pre-push"
            target = temp_root / ".git" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("axis hook\n", encoding="utf-8")

            def fake_run(args: list[str], **_kwargs):
                if args[1:] in (
                    ["config", "--get", "core.hooksPath"],
                    ["config", "--local", "--get", "core.hooksPath"],
                ):
                    return axis.subprocess.CompletedProcess(args, 1, stdout="", stderr="")
                if args[1:] == ["rev-parse", "--git-common-dir"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout=f"{temp_root / '.git'}\n", stderr=""
                    )
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

            self.assertFalse(outside.exists())
            self.assertEqual("axis hook\n", target.read_text(encoding="utf-8"))

    def test_replaces_repo_core_hooks_path_with_git_hook_copy(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            target = temp_root / ".git" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            source.write_text("#!/usr/bin/env python3\nprint('pre-push')\n", encoding="utf-8")

            calls: list[list[str]] = []
            local_hooks_configured = True

            def fake_run(args: list[str], **_kwargs):
                nonlocal local_hooks_configured
                calls.append(args)
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    if local_hooks_configured:
                        return axis.subprocess.CompletedProcess(
                            args, 0, stdout="scripts/hooks\n", stderr=""
                        )
                    return axis.subprocess.CompletedProcess(args, 1, stdout="", stderr="")
                if args[1:] == ["config", "--local", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout="scripts/hooks\n", stderr=""
                    )
                if args[1:] == ["config", "--show-scope", "--get-all", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout="local\tscripts/hooks\n", stderr=""
                    )
                if args[1:] == ["config", "--local", "--unset-all", "core.hooksPath"]:
                    local_hooks_configured = False
                    return axis.subprocess.CompletedProcess(args, 0, stdout="", stderr="")
                if args[1:] == ["rev-parse", "--git-path", "hooks/pre-push"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout=f"{target}\n", stderr="")
                if args[1:] == ["rev-parse", "--git-common-dir"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout=f"{temp_root / '.git'}\n", stderr=""
                    )
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
            marker = target.with_name("pre-push.axis-managed.sha256")
            self.assertEqual(
                f"{hashlib.sha256(source.read_bytes()).hexdigest()}\n",
                marker.read_text(encoding="utf-8"),
            )
            if axis.os.name != "nt":
                self.assertNotEqual(0, target.stat().st_mode & 0o111)
            self.assertIn(
                ["git", "config", "--local", "--unset-all", "core.hooksPath"],
                calls,
            )

    def test_refuses_to_overwrite_an_unmanaged_default_pre_push_hook(self) -> None:
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            temp_root = Path(temp)
            source = temp_root / "scripts" / "hooks" / "pre-push"
            target = temp_root / ".git" / "hooks" / "pre-push"
            source.parent.mkdir(parents=True)
            target.parent.mkdir(parents=True)
            source.write_text("axis hook\n", encoding="utf-8")
            target.write_text("personal hook\n", encoding="utf-8")

            def fake_run(args: list[str], **_kwargs):
                if args[1:] == ["config", "--get", "core.hooksPath"]:
                    return axis.subprocess.CompletedProcess(args, 1, stdout="", stderr="")
                if args[1:] == ["rev-parse", "--git-path", "hooks/pre-push"]:
                    return axis.subprocess.CompletedProcess(args, 0, stdout=f"{target}\n", stderr="")
                if args[1:] == ["rev-parse", "--git-common-dir"]:
                    return axis.subprocess.CompletedProcess(
                        args, 0, stdout=f"{temp_root / '.git'}\n", stderr=""
                    )
                raise AssertionError(f"unexpected command: {args}")

            original_root = axis.ROOT
            axis.ROOT = temp_root
            try:
                with (
                    mock.patch.object(axis, "run", side_effect=fake_run),
                    mock.patch.object(axis, "exe", side_effect=lambda name: name),
                    contextlib.redirect_stderr(io.StringIO()) as stderr,
                ):
                    self.assertEqual(1, axis.install_hooks())
            finally:
                axis.ROOT = original_root

            self.assertEqual("personal hook\n", target.read_text(encoding="utf-8"))
            self.assertIn("refusing to overwrite unmanaged hook", stderr.getvalue())


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

if __name__ == "__main__":
    unittest.main()
