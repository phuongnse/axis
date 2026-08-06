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
project_orchestration = load_python_file(ROOT / ".codex" / "check.py")
named_agent_hook = load_python_file(ROOT / ".codex" / "hooks" / "require_named_agent.py")


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

    def test_rejects_pending_requirement_on_human_branch(self) -> None:
        body = """## Summary
This summary is long enough.

## Linked spec
docs/use-cases/example/README.md

## Requirements & rules followed
- [ ] **Verification gate** - local checks [status: pending]
"""
        self.assertIn(
            "Pending requirement is not publishable",
            "\n".join(check_pr.validate("feat(example): improve gates", body, "feat/improve-gates")),
        )

    def test_accepts_pending_requirement_on_renovate_branch(self) -> None:
        body = """## Summary
Dependency update.

## Linked spec
N/A

## Requirements & rules followed
- [ ] **Review readiness** - awaits review [status: pending]
- [ ] **Verification** - awaits CI [status: pending]
"""

        self.assertEqual(
            [],
            check_pr.validate("chore(deps): update packages", body, "renovate/all-non-major"),
        )

    def test_accepts_checked_requirement(self) -> None:
        body = """## Summary
This summary is long enough.

## Linked spec
docs/use-cases/example/README.md

## Requirements & rules followed
- [x] **Verification gate** - local checks [status: satisfied]
"""
        self.assertEqual([], check_pr.validate("feat(example): improve gates", body))

    def test_rejects_pending_checked_requirement(self) -> None:
        body = """## Summary
Summary.

## Linked spec
N/A

## Requirements & rules followed
- [x] **Verification gate** - waiting for CI [status: pending]
"""

        self.assertIn("Pending requirement must be unchecked", "\n".join(check_pr.validate("fix: x", body)))

    def test_rejects_not_applicable_without_structured_reason(self) -> None:
        body = """## Summary
Summary.

## Linked spec
N/A

## Requirements & rules followed
- [x] **Spec/code** [status: not-applicable]
"""

        self.assertIn("must include `[reason: ...]`", "\n".join(check_pr.validate("fix: x", body)))


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
                    "`python scripts/axis.py doctor --unsupported <profile>`"
                ),
            }
        )

        self.assertIn("unknown option `--unsupported`", issues)

    def test_rejects_invalid_documented_commands_before_shell_redirection(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py doctor --unsupported > doctor.txt`"
                ),
            }
        )

        self.assertIn("unrecognized arguments: --unsupported", issues)

    def test_redirection_target_placeholder_does_not_hide_invalid_command(self) -> None:
        issues = self.documented_issue_text(
            {
                "docs/playbooks/example.md": (
                    "`python scripts/axis.py doctor --unsupported > <doctor-output>`"
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
                        "python3 scripts/axis.py doctor",
                        "```",
                        "```powershell",
                        "py -3 scripts/axis.py doctor",
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
        accepted_on: str = "2026-07-22",
        expires_on: str = "2026-08-21",
    ) -> None:
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
            (frontend / ".nvmrc").write_text(f"{axis.axis_setup.NODE_VERSION}\n", encoding="utf-8")
            (frontend / "Dockerfile.dev").write_text(
                f"FROM node:{axis.axis_setup.NODE_VERSION}-alpine\n",
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
            (frontend / ".nvmrc").write_text(f"{axis.axis_setup.NODE_VERSION}\n", encoding="utf-8")
            (frontend / "Dockerfile.dev").write_text(
                f"FROM node:{axis.axis_setup.NODE_VERSION}-alpine\n",
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
            self.write_frontend_risk_acceptance(root, expires_on="2026-08-22")
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
            mock.patch.object(axis.axis_setup, "DOTNET_SDK_VERSION", "9.0.100"),
            mock.patch.object(axis, "command_version_line") as command_version,
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("portable setup pins 9.0.100", detail)
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
        self.assertIn("axis.py setup --profile build --install-user-tools", detail)

    def test_dotnet_sdk_missing_runtime_points_to_axis_managed_setup(self) -> None:
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(False, "dotnet not found", "dotnet"),
            ),
            mock.patch.object(axis.axis_setup, "dotnet_native_prerequisite_hint", return_value=None),
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        self.assertIn("axis.py setup --profile build --install-user-tools", detail)

    def test_dotnet_sdk_surfaces_classified_native_prerequisite(self) -> None:
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(False, "Couldn't find a valid ICU package installed on the system.", "/tools/dotnet"),
            ),
            mock.patch.object(
                axis.axis_setup,
                "dotnet_native_prerequisite_hint",
                return_value=(
                    "the .NET host is missing ICU; on Ubuntu 26.04 install it with "
                    "`sudo apt install libicu78`; Axis will not run sudo or an OS package manager"
                ),
                create=True,
            ) as classify,
        ):
            ok, detail = axis.dotnet_sdk_status()

        self.assertFalse(ok)
        classify.assert_called_once()
        self.assertIn("sudo apt install libicu78", detail)

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
        self.assertIn("axis.py setup --profile build --install-user-tools", detail)

    def test_wrong_npm_points_to_axis_managed_setup(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(True, "11.15.0", "/usr/bin/npm"),
        ):
            ok, detail = axis.npm_version_status({})

        self.assertFalse(ok)
        self.assertIn("axis.py setup --profile build --install-user-tools", detail)

    def test_path_node_toolchain_requires_exact_colocated_node_and_npm(self) -> None:
        node = "/tools/node/bin/node"
        npm = "/tools/node/bin/npm"

        def which(name: str, *_args, **_kwargs):
            return {"node": node, "npm": npm}.get(name)

        def probe(command: list[str], **_kwargs):
            output = "v24.18.0\n" if command[0] == node else "11.16.0\n"
            return axis.subprocess.CompletedProcess(command, 0, stdout=output, stderr="")

        with (
            mock.patch.object(axis.shutil, "which", side_effect=which),
            mock.patch.object(axis, "required_node_version", return_value=(True, "24.18.0")),
            mock.patch.object(axis, "run_optional", side_effect=probe),
        ):
            self.assertTrue(axis.path_node_toolchain_ready())

    def test_path_node_toolchain_rejects_windows_npm_without_linux_node(self) -> None:
        with mock.patch.object(
            axis.shutil,
            "which",
            side_effect=lambda name, *_args, **_kwargs: "/mnt/c/Program Files/nodejs/npm" if name == "npm" else None,
        ):
            self.assertFalse(axis.path_node_toolchain_ready())

    def test_build_doctor_rejects_wrong_npm_version(self) -> None:
        with (
            mock.patch.object(axis, "python_launcher_status", return_value=("OK", "Python 3.12.3")),
            mock.patch.object(axis, "_command_version", return_value=("OK", "git version 2.43.0")),
            mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "10.0.302")),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={"PATH": "/managed/node"}),
            mock.patch.object(axis, "node_version_status", return_value=(True, "v24.18.0")),
            mock.patch.object(
                axis,
                "npm_version_status",
                return_value=(
                    False,
                    "found npm `11.15.0`; expected 11.16.0; "
                    "run `python scripts/axis.py setup --profile build --install-user-tools`",
                ),
            ),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            rc = axis.doctor(axis.argparse.Namespace(profile="build", strict=True))

        self.assertEqual(1, rc)
        self.assertIn("[FAIL] npm: found npm `11.15.0`", stdout.getvalue())
        self.assertIn("npm", stderr.getvalue())

    def test_missing_lychee_points_to_axis_managed_setup(self) -> None:
        with (
            mock.patch.object(axis, "find_lychee", return_value=None),
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            rc = axis.check_markdown_links_for_paths([])

        self.assertEqual(1, rc)
        self.assertIn("axis.py setup --profile review --install-user-tools", stderr.getvalue())

    def test_frontend_toolchain_env_resolves_nvm_when_path_lacks_node(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            nvm_root = root / ".nvm"
            older_bin = nvm_root / "versions" / "node" / "v24.17.0" / "bin"
            expected_bin = nvm_root / "versions" / "node" / "v24.18.0" / "bin"
            path_dir = root / "plain-path"
            for bin_dir in (older_bin, expected_bin):
                bin_dir.mkdir(parents=True)
                (bin_dir / "node").write_text("", encoding="utf-8")
                (bin_dir / "npm").write_text("", encoding="utf-8")
            path_dir.mkdir()

            def fake_command_version_line(name: str, *_args: str, env: dict[str, str] | None = None):
                path = env.get("PATH", "") if env else ""
                if path.split(axis.os.pathsep)[0] == str(expected_bin):
                    version = "v24.18.0" if name == "node" else "11.16.0"
                    return True, version, str(expected_bin / name)
                return False, f"{name} not found in PATH", name

            with (
                mock.patch.dict(axis.os.environ, {"NVM_DIR": str(nvm_root), "PATH": str(path_dir)}, clear=True),
                mock.patch.object(axis, "required_node_version", return_value=(True, "24.18.0")),
                mock.patch.object(axis, "command_version_line", side_effect=fake_command_version_line),
            ):
                env = axis.frontend_toolchain_env()

        self.assertEqual(str(expected_bin), env["PATH"].split(axis.os.pathsep)[0])

    def test_frontend_toolchain_env_resolves_nvm_windows_when_path_lacks_node(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            nvm_home = root / "nvm"
            expected_dir = nvm_home / "v24.18.0"
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
                version = "v24.18.0" if name == "node" else "11.16.0"
                suffix = "node.exe" if name == "node" else "npm.cmd"
                return True, version, str(expected_dir / suffix)

            with (
                mock.patch.object(axis, "_nvm_unix_roots", return_value=[]),
                mock.patch.object(axis, "_nvm_windows_roots", return_value=[nvm_home]),
                mock.patch.object(axis.Path, "home", return_value=root),
                mock.patch.dict(axis.os.environ, {"PATH": str(path_dir)}, clear=True),
                mock.patch.object(axis, "required_node_version", return_value=(True, "24.18.0")),
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

    def test_local_dev_doctor_does_not_require_host_playwright(self) -> None:
        playwright_status = mock.Mock(return_value=(False, "missing host browser"))
        with (
            mock.patch.object(axis, "find_lychee", return_value="/usr/bin/lychee"),
            mock.patch.object(axis, "lychee_version_status", return_value=(True, "lychee 0.23.0")),
            mock.patch.object(axis, "dotnet_sdk_status", return_value=(True, "10.0.302")),
            mock.patch.object(axis, "frontend_toolchain_env", return_value={}),
            mock.patch.object(axis, "node_version_status", return_value=(True, "v24.18.0")),
            mock.patch.object(axis, "playwright_chromium_status", playwright_status),
            mock.patch.object(axis, "python_launcher_status", return_value=("OK", "Python 3.14.4")),
            mock.patch.object(axis, "_command_version", return_value=("OK", "/usr/bin/tool")),
            mock.patch.object(axis, "find_openssl", return_value="/usr/bin/openssl"),
            mock.patch.object(axis, "_docker_info_ok", return_value=True),
            mock.patch.object(axis, "_docker_host_ping_ok", return_value=False),
            mock.patch.object(axis, "_http_ok", return_value=False),
            mock.patch.object(axis, "_wsl_docker_ok", return_value=False),
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "local_dev_certificates_valid", return_value=True),
            mock.patch.object(
                axis,
                "local_dev_host_trust_status",
                return_value=("WARN", "host browser trust is not configured"),
                create=True,
            ) as trust_status,
            contextlib.redirect_stdout(io.StringIO()) as stdout,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(0, axis.doctor(axis.argparse.Namespace(strict=True)))

        playwright_status.assert_not_called()
        trust_status.assert_called_once_with()
        self.assertNotIn("playwright chromium", stdout.getvalue())
        self.assertIn("local HTTPS certificates", stdout.getvalue())
        self.assertIn("host browser trust", stdout.getvalue())
        self.assertEqual("", stderr.getvalue())

    def test_core_doctor_profile_skips_build_local_dev_and_review_tools(self) -> None:
        with (
            mock.patch.object(axis.shutil, "which", return_value="/usr/bin/tool"),
            mock.patch.object(axis, "python_launcher_status", return_value=("OK", "Python 3.14.4")),
            mock.patch.object(axis, "_command_version", return_value=("OK", "tool 1.0")),
            mock.patch.object(axis, "dotnet_sdk_status") as dotnet,
            mock.patch.object(axis, "frontend_toolchain_env") as frontend,
            mock.patch.object(axis, "_docker_info_ok") as docker,
            mock.patch.object(axis, "find_lychee") as lychee,
            contextlib.redirect_stdout(io.StringIO()) as stdout,
        ):
            self.assertEqual(
                0,
                axis.doctor(axis.argparse.Namespace(profile="core", strict=True)),
            )

        self.assertIn("profile=core", stdout.getvalue())
        for skipped in (dotnet, frontend, docker, lychee):
            skipped.assert_not_called()

    def test_core_doctor_requires_canonical_python_launcher(self) -> None:
        with (
            mock.patch.object(axis, "python_launcher_status", return_value=("FAIL", "python not found in PATH")),
            mock.patch.object(axis, "_command_version", return_value=("OK", "git version 2.53.0")),
            contextlib.redirect_stdout(io.StringIO()) as stdout,
            contextlib.redirect_stderr(io.StringIO()) as stderr,
        ):
            self.assertEqual(1, axis.doctor(axis.argparse.Namespace(profile="core", strict=True)))

        self.assertIn("[FAIL] python launcher: python not found in PATH", stdout.getvalue())
        self.assertIn("python launcher", stderr.getvalue())

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

        self.assertEqual(["toolchain", "versions", "audit", "ci", "test"], calls)

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
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.verify(object()))

        self.assertEqual(["toolchain", "versions", "audit", "ci", "test"], calls)

    def test_frontend_source_change_runs_version_and_vulnerability_gates(self) -> None:
        calls: list[str] = []
        with (
            mock.patch.object(
                axis,
                "verify_scope_paths",
                return_value=("working tree", ["frontend/src/app.tsx"]),
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

    def test_project_orchestration_change_runs_repo_skills_gate(self) -> None:
        for path in (".codex/config.toml", ".gitignore"):
            with self.subTest(path=path):
                with (
                    mock.patch.object(axis, "verify_scope_paths", return_value=("working tree", [path])),
                    mock.patch.object(axis, "run_text_encoding_check", return_value=0),
                    mock.patch.object(axis, "check_repo_skills", return_value=0) as repo_skills,
                    contextlib.redirect_stdout(io.StringIO()),
                ):
                    self.assertEqual(0, axis.verify(object()))

                repo_skills.assert_called_once_with()


class TestReviewVerificationGates(unittest.TestCase):
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

    def test_rejects_dirty_worktree_before_running_checks(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=["scripts/axis.py"]),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_review_readiness_policy") as policy,
            contextlib.redirect_stderr(io.StringIO()),
        ):
            result = axis.review_readiness(axis.argparse.Namespace(since=None, policy_only=False))

        self.assertEqual(1, result)
        verify.assert_not_called()
        policy.assert_not_called()

    def test_runs_verify_and_shared_policy_profile(self) -> None:
        with (
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["frontend/src/App.tsx"])),
            mock.patch.object(axis, "verify", return_value=0) as verify,
            mock.patch.object(axis, "run_review_readiness_policy", return_value=(0, ["doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.review_readiness(axis.argparse.Namespace(since=None, policy_only=False))

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
            mock.patch.object(axis, "working_tree_paths", return_value=[]),
            mock.patch.object(axis, "verify_scope_paths", return_value=("base...HEAD", ["scripts/axis.py"])),
            mock.patch.object(axis, "verify") as verify,
            mock.patch.object(axis, "run_review_readiness_policy", return_value=(0, ["policy gate tests", "doc drift"])) as policy,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.review_readiness(axis.argparse.Namespace(since=None, policy_only=True))

        self.assertEqual(0, result)
        verify.assert_not_called()
        policy.assert_called_once_with(
            ["scripts/axis.py"],
            policy_tests_covered=False,
            doc_drift_covered=set(),
            doc_drift_range=None,
        )

    def test_policy_registry_routes_only_triggered_expensive_checks(self) -> None:
        names = [
            name
            for name, _checker in axis.review_readiness_policy_gates(
                ["scripts/axis.py", ".github/renovate.json5"]
            )
        ]

        self.assertEqual(["policy gate tests", "Renovate config", "doc drift"], names)
        self.assertEqual(
            ["doc drift"],
            [
                name
                for name, _checker in axis.review_readiness_policy_gates(
                    ["frontend/src/App.tsx"],
                    policy_tests_covered=True,
                )
            ],
        )

    def test_review_readiness_reuses_verify_coverage_in_doc_drift(self) -> None:
        paths = [
            "scripts/axis.py",
            ".agents/skills/axis-script-scope/SKILL.md",
            "docs/use-cases/example.md",
            "docs/foundations/example.md",
        ]

        self.assertEqual(
            {
                "check-text-encoding",
                "check-scripts-standard",
                "check-repo-skills",
                "check-doc-navigation",
                "check-doc-size-budgets",
                "check-doc-code-fences.py",
                "check-use-case-docs.py",
                "check-foundation-docs.py",
            },
            axis.review_readiness_doc_drift_coverage(paths),
        )
        self.assertIn(
            "check-theme",
            axis.review_readiness_doc_drift_coverage(["theme/axis-theme.json"]),
        )

    def test_doc_drift_selects_only_checkers_for_touched_surfaces(self) -> None:
        selected = axis.doc_drift_checker_names(
            [
                "scripts/axis.py",
                ".agents/skills/reference.md",
                ".codex/config.toml",
                "docs/playbooks/scripts.md",
            ]
        )

        self.assertEqual(
            {
                "check-text-encoding",
                "check-scripts-standard",
                "check-repo-skills",
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
        covered = {"check-repo-skills"}
        gates = dict(
            axis.review_readiness_policy_gates(
                [".agents/skills/axis-example/SKILL.md"],
                doc_drift_covered=covered,
                doc_drift_range="base..HEAD",
            )
        )

        with mock.patch.object(axis, "check_doc_drift", return_value=0) as doc_drift:
            self.assertEqual(0, gates["doc drift"]())

        args = doc_drift.call_args.args[0]
        self.assertEqual(covered, args.skip_checkers)
        self.assertEqual([".agents/skills/axis-example/SKILL.md"], args.paths)
        self.assertEqual("base..HEAD", args.range_spec)

    def test_pre_push_full_delegates_to_review_readiness(self) -> None:
        with (
            mock.patch.dict(axis.os.environ, {"AXIS_PRE_PUSH_FULL": "1"}),
            mock.patch.object(axis, "review_readiness", return_value=0) as review_readiness,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            result = axis.pre_push(object())

        self.assertEqual(0, result)
        review_readiness.assert_called_once()
        delegated = review_readiness.call_args.args[0]
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

    def test_runs_changed_frontend_e2e_file_for_e2e_only_change(self) -> None:
        calls: list[str] = []
        browser_runner = mock.Mock(return_value=0)

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
        browser_runner.assert_called_once_with(["e2e/register.pw.ts"])

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


class TestGitWorkflows(unittest.TestCase):
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
                    axis.argparse.Namespace(branch="renovate/all-non-major")
                ),
            )

        self.assertIn(
            [
                "fetch",
                "--no-tags",
                "origin",
                "refs/heads/renovate/all-non-major:"
                "refs/remotes/origin/renovate/all-non-major",
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
            ["dotnet", "build", project, "--nologo", "--no-restore"],
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

    def test_setup_restores_locked_dependencies_and_optional_browser(self) -> None:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0)

        def fake_frontend(args: list[str]):
            calls.append(["npm", *args])
            return axis.subprocess.CompletedProcess(args, 0)

        with (
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=0),
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

    def test_setup_exposes_managed_node_commands_when_path_toolchain_is_not_ready(self) -> None:
        exposed = (Path("/users/alice/.local/bin/node"), Path("/users/alice/.local/bin/npm"))
        with (
            mock.patch.object(axis, "path_node_toolchain_ready", return_value=False),
            mock.patch.object(axis, "path_dotnet_sdk_ready", return_value=True),
            mock.patch.object(axis, "setup_tool_ready", return_value=True),
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=0),
            mock.patch.object(axis.axis_setup, "confirm_install"),
            mock.patch.object(axis.axis_setup, "expose_managed_commands", return_value=exposed) as expose,
            mock.patch.object(axis, "run", return_value=axis.subprocess.CompletedProcess([], 0)),
            mock.patch.object(axis, "run_frontend_npm", return_value=axis.subprocess.CompletedProcess([], 0)),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            rc = axis.setup(
                axis.argparse.Namespace(
                    profile="build",
                    browsers=False,
                    install_user_tools=True,
                    plan_only=False,
                    trust_local_ca=False,
                    yes=True,
                )
            )

        self.assertEqual(0, rc)
        expose.assert_called_once_with(("node", "npm"), platform_spec=mock.ANY)

    def test_setup_fails_before_mutating_when_a_toolchain_is_missing(self) -> None:
        with (
            mock.patch.object(axis, "setup_external_preflight", return_value=0),
            mock.patch.object(axis, "setup_preflight", return_value=1),
            mock.patch.object(axis, "run") as run,
            mock.patch.object(axis, "run_frontend_npm") as run_npm,
            contextlib.redirect_stdout(io.StringIO()),
            contextlib.redirect_stderr(io.StringIO()),
        ):
            self.assertEqual(1, axis.setup(axis.argparse.Namespace(browsers=False)))

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
                dotnet_args=[project, "--", "--filter", "FullyQualifiedName~RuleAssetDefinitionHandlerTests"],
            ),
        )

        self.assertEqual(
            [
                "dotnet",
                "test",
                project,
                "--nologo",
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

    def test_migration_add_uses_owned_module_contracts(self) -> None:
        targets = {
            "audit": (
                "Audit/Axis.Audit.Infrastructure/"
                "Axis.Audit.Infrastructure.csproj",
                "AuditDbContext",
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
                    ],
                    calls[0],
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

    def test_migration_add_uses_non_routable_design_time_connection(self) -> None:
        with (
            mock.patch.object(axis, "check_dotnet_sdk", return_value=0),
            mock.patch.object(
                axis,
                "run",
                return_value=axis.subprocess.CompletedProcess([], 0),
            ) as run,
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
            self.assertIn("subjectAltName=@alt_names", (cert_dir / "localhost.ext").read_text(encoding="utf-8"))
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
                    if "basicConstraints" in command:
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

            def fake_run(command: list[str], **_kwargs):
                calls.append(command)
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
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(0, axis.main(["local-dev", "certs", "--renew"]))
            self.assertEqual(0, axis.main(["local-dev", "trust-certs", "--yes"]))

        self.assertTrue(certs.call_args.args[0].renew)
        self.assertTrue(trust.call_args.args[0].yes)


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
            ".agents/skills/reference.md": (
                "# Contract\n\nRoute durable guidance before edit.\n\n"
                "The entry domain owner keeps spec, status, and evidence decisions. Other durable "
                "guidance **Requires** selecting `$axis-doc-hygiene` or entering it through a typed "
                "handoff before edit.\n"
            ),
            ".agents/skills/workflows.toml": (
                "version = 1\n\n"
                "[workflows.example]\n"
                'initial = "start"\n'
                'terminal_states = ["done"]\n'
                'states = ["start", "done"]\n\n'
                "[[workflows.example.transitions]]\n"
                'id = "perform"\n'
                'from = "start"\n'
                'to = "done"\n'
                'owner = "skill:axis-example"\n'
                'evidence = "example-result"\n'
            ),
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

    def add_skill(self, files: dict[str, str], name: str) -> None:
        files[f".agents/skills/{name}/SKILL.md"] = files[
            ".agents/skills/axis-example/SKILL.md"
        ].replace("axis-example", name).replace("Axis Example", name.replace("-", " ").title())
        files[".agents/skills/README.md"] += (
            f"| {name} | [{name}/SKILL.md](./{name}/SKILL.md) |\n"
        )

    def test_accepts_valid_repo_skill(self) -> None:
        self.assertEqual([], self.issues_for_skill(self.valid_skill_files()))

    def test_rejects_unknown_workflow_skill_owner(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/workflows.toml"] = files[
            ".agents/skills/workflows.toml"
        ].replace("skill:axis-example", "skill:axis-missing")

        issues = self.issues_for_skill(files)

        self.assertIn("references unknown skill owner `axis-missing`", "\n".join(issues))

    def test_rejects_multiple_owners_for_one_workflow_transition(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/workflows.toml"] += (
            "\n[[workflows.example.transitions]]\n"
            'id = "perform-again"\n'
            'from = "start"\n'
            'to = "done"\n'
            'owner = "skill:axis-example"\n'
            'evidence = "other-result"\n'
        )

        issues = self.issues_for_skill(files)

        self.assertIn("transition `start -> done` has multiple owners", "\n".join(issues))

    def test_rejects_unreachable_workflow_state(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/workflows.toml"] = files[
            ".agents/skills/workflows.toml"
        ].replace('states = ["start", "done"]', 'states = ["start", "done", "orphan"]')

        issues = self.issues_for_skill(files)

        self.assertIn("state `orphan` is unreachable from `start`", "\n".join(issues))

    def test_rejects_workflow_state_without_terminal_path(self) -> None:
        files = self.valid_skill_files()
        files[".agents/skills/workflows.toml"] = (
            "version = 1\n\n"
            "[workflows.example]\n"
            'initial = "start"\n'
            'terminal_states = ["done"]\n'
            'states = ["start", "dead-end", "done"]\n\n'
            "[[workflows.example.transitions]]\n"
            'id = "finish"\n'
            'from = "start"\n'
            'to = "done"\n'
            'owner = "skill:axis-example"\n'
            'evidence = "done-result"\n\n'
            "[[workflows.example.transitions]]\n"
            'id = "enter-dead-end"\n'
            'from = "start"\n'
            'to = "dead-end"\n'
            'owner = "skill:axis-example"\n'
            'evidence = "dead-end-result"\n\n'
            "[[workflows.example.transitions]]\n"
            'id = "remain-stuck"\n'
            'from = "dead-end"\n'
            'to = "dead-end"\n'
            'owner = "skill:axis-example"\n'
            'evidence = "stuck-result"\n'
        )

        issues = self.issues_for_skill(files)

        self.assertIn("state `dead-end` cannot reach a terminal state", "\n".join(issues))

    def test_accepts_semantic_role_owner_backed_by_project_agent(self) -> None:
        files = self.valid_skill_files()
        files[".codex/agents/axis_reviewer.toml"] = 'name = "axis_reviewer"\n'
        files[".agents/skills/workflows.toml"] = (
            "version = 1\n\n"
            "[semantic_roles]\n"
            'independent-reviewer = "axis_reviewer"\n\n'
            "[workflows.review]\n"
            'initial = "ready"\n'
            'terminal_states = ["reviewed"]\n'
            'states = ["ready", "reviewed"]\n\n'
            "[[workflows.review.transitions]]\n"
            'id = "review"\n'
            'from = "ready"\n'
            'to = "reviewed"\n'
            'owner = "role:independent-reviewer"\n'
            'evidence = "review-result"\n'
        )

        self.assertEqual([], self.issues_for_skill(files))

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

    def test_current_project_orchestration_is_valid(self) -> None:
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(0, axis.check_project_orchestration())

    def test_nested_project_agent_role_is_discovered_for_rejection(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            nested = root / ".codex" / "agents" / "nested"
            nested.mkdir(parents=True)
            (nested / "rogue.toml").write_text('name = "rogue"\n', encoding="utf-8")

            self.assertEqual({"nested/rogue.toml"}, project_orchestration.project_agent_role_files(root))

    def test_default_agent_fallback_is_rejected(self) -> None:
        self.assertEqual(
            ["default_subagent_model", "default_subagent_reasoning_effort"],
            project_orchestration.unexpected_default_agent_keys(
                {
                    "enabled": True,
                    "default_subagent_model": "generic",
                    "default_subagent_reasoning_effort": "generic",
                }
            ),
        )
        self.assertEqual([], project_orchestration.unexpected_default_agent_keys({"enabled": True}))

    def test_named_agent_hook_rejects_missing_default_and_unknown_roles(self) -> None:
        allowed = frozenset({"axis_scout", "axis_worker"})
        for agent_type in (None, "default", "unknown"):
            tool_input = {} if agent_type is None else {"agent_type": agent_type}
            decision = named_agent_hook.policy_decision(
                {
                    "hook_event_name": "PreToolUse",
                    "tool_name": "Agent",
                    "tool_input": tool_input,
                },
                allowed_agent_types=allowed,
            )

            self.assertEqual("deny", decision["hookSpecificOutput"]["permissionDecision"])

    def test_named_agent_hook_allows_only_project_roles(self) -> None:
        allowed = named_agent_hook.configured_agent_types()
        for agent_type in allowed:
            self.assertIsNone(
                named_agent_hook.policy_decision(
                    {
                        "hook_event_name": "PreToolUse",
                        "tool_name": "Agent",
                        "tool_input": {"agent_type": agent_type},
                    },
                    allowed_agent_types=allowed,
                )
            )
        self.assertEqual(
            {path.removesuffix(".toml") for path in project_orchestration.project_agent_role_files()},
            set(allowed),
        )


class TestDoctorPythonPackageChecks(unittest.TestCase):
    def test_python_launcher_status_rejects_python_2(self) -> None:
        with mock.patch.object(
            axis,
            "command_version_line",
            return_value=(True, "Python 2.7.18", "/usr/bin/python"),
        ):
            status, detail = axis.python_launcher_status()

        self.assertEqual("FAIL", status)
        self.assertIn("expected Python 3", detail)

    def test_python_launcher_status_requires_tar_data_filter(self) -> None:
        probe = axis.subprocess.CompletedProcess(
            ["/usr/bin/python", "-c", "probe"],
            1,
            stdout="",
            stderr="",
        )
        with (
            mock.patch.object(
                axis,
                "command_version_line",
                return_value=(True, "Python 3.11.0", "/usr/bin/python"),
            ),
            mock.patch.object(axis, "run_optional", return_value=probe),
        ):
            status, detail = axis.python_launcher_status()

        self.assertEqual("FAIL", status)
        self.assertIn("tar data extraction filter", detail)

    def test_python_launcher_status_checks_tar_filter_when_optimized(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            Path(temp, "tarfile.py").write_text(
                "class TarFile:\n    def extractall(self): pass\n",
                encoding="utf-8",
            )
            with (
                mock.patch.object(
                    axis,
                    "command_version_line",
                    return_value=(True, f"Python {axis.platform.python_version()}", axis.sys.executable),
                ),
                mock.patch.dict(
                    axis.os.environ,
                    {"PYTHONOPTIMIZE": "1", "PYTHONPATH": temp},
                ),
            ):
                status, detail = axis.python_launcher_status()

        self.assertEqual("FAIL", status)
        self.assertIn("tar data extraction filter", detail)

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
