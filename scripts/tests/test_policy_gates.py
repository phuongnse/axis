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
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


check_pr = load_script("check-pr.py")
check_use_case_docs = load_script("check-use-case-docs.py")


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
    def issues_for_document(self, content: str) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "docs" / "use-cases" / "example" / "sample" / "README.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
            original_root = check_use_case_docs.ROOT
            check_use_case_docs.ROOT = root
            try:
                return check_use_case_docs.check_file(path)
            finally:
                check_use_case_docs.ROOT = original_root

    def issues_for_use_case(self, callout: str, ac_line: str = "- **AC-001** Works.") -> list[str]:
        matrix = (
            ""
            if "## Acceptance Test Matrix" in callout
            else """## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

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

## Design System

| Surface | Contract |
|---|---|
| N/A | N/A |

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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

| ID | Level | Scenario | Covers AC | Evidence source | Automated by | Required to close |
|---|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Main flow | Playwright | Yes |

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

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | `frontend/e2e/sample.pw.ts` | Yes |
| AT-002 | API | Backend side effect | AC-001 | Axis.Api.Tests | Yes |
| AT-003 | UI | UI validation | AC-001 | npm run test | Yes |

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

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |
| AT-002 | API | Backend side effect | AC-001 | xUnit API | Yes |

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

    def test_rejects_acceptance_matrix_unknown_and_uncovered_ac_ids(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-999 | Playwright | No |

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

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
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
        self.assertIn("invalid Level `Smoke`", joined)
        self.assertIn("invalid Automated by `Jest`", joined)
        self.assertIn("Required to close must be `Yes` or `No`", joined)

    def test_rejects_acceptance_matrix_mixed_id_prefixes(self) -> None:
        issues = self.issues_for_use_case(
            """## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |
| REG-002 | API | Backend side effect | AC-001 | xUnit API | Yes |

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

    def test_rejects_design_sources_table_schema_drift(self) -> None:
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

## Design Sources

| Screen | Preview |
|--------|---------|
| N/A | N/A |

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

        self.assertIn("Design Sources table missing required columns: Source", "\n".join(issues))

    def test_accepts_design_sources_table_with_source_column(self) -> None:
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

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

## Out Of Scope

- N/A.

## Design System

| Surface | Contract |
|---|---|
| N/A | N/A |

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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

    def test_rejects_implementation_status_before_design_sources(self) -> None:
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

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

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

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |
"""
        )

        self.assertIn("section order must be", "\n".join(issues))

    def test_rejects_design_source_pointing_to_preview_asset(self) -> None:
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

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

## Out Of Scope

- N/A.

## Design System

| Surface | Contract |
|---|---|
| register | Uses shared UI primitives. |

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| register | [source](./register.svg) | [preview](./register.svg) |

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

        self.assertIn("must link to an editable design source", "\n".join(issues))

    def test_rejects_design_source_preview_without_editable_source(self) -> None:
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

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

## Out Of Scope

- N/A.

## Design System

| Surface | Contract |
|---|---|
| register | Uses shared UI primitives. |

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| register | N/A | [preview](./register.png) |

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

        self.assertIn("has a preview but no editable Source", "\n".join(issues))

    def test_accepts_external_design_source_with_preview(self) -> None:
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

## Acceptance Test Matrix

| ID | Level | Scenario | Covers AC | Automated by | Required to close |
|---|---|---|---|---|---|
| AT-001 | E2E | User completes flow | AC-001 | Playwright | Yes |

## Out Of Scope

- N/A.

## Design System

| Surface | Contract |
|---|---|
| register | Uses shared UI primitives. |

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| register | [source](https://design.example/register) | [preview](./register.svg) |

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

## Design Sources
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

    def stale_reference_text(self, files: dict[str, str]) -> str:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return "\n".join(axis.stale_reference_issues(root=root))

    def test_rejects_claude_reference_in_entry_guidance(self) -> None:
        issues = self.stale_reference_text({"AGENTS.md": "canonical rules: CLAUDE.md\n"})

        self.assertIn("AGENTS.md is the agent contract", issues)

    def test_rejects_claude_reference_in_skill_guidance(self) -> None:
        issues = self.stale_reference_text({".agents/skills/example/SKILL.md": "Read CLAUDE.md first.\n"})

        self.assertIn("AGENTS.md is the agent contract", issues)

    def test_current_repository_stale_references_still_pass(self) -> None:
        self.assertEqual([], axis.stale_reference_issues())

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


class TestToolVersionGates(unittest.TestCase):
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
                "exec vitest run frontend/tests/button.test.tsx",
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


class TestFrontendRadiusTokens(unittest.TestCase):
    def issues_for_files(self, files: dict[str, str], css_radius: str = "0.5rem") -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            css = root / "frontend" / "src" / "design-system" / "tokens.css"
            css.parent.mkdir(parents=True, exist_ok=True)
            css.write_text(f":root {{\n  --radius: {css_radius};\n}}\n", encoding="utf-8")
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_radius_token_issues(root=root)

    def issues_for_frontend(self, component_source: str, css_radius: str = "0.5rem") -> list[str]:
        return self.issues_for_files(
            {"frontend/src/components/Example.tsx": component_source},
            css_radius=css_radius,
        )

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

    def test_accepts_upstream_radius_in_shadcn_sourced_primitive(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/components/ui/button.tsx": (
                    "export const upstream = 'rounded-xl rounded-[4px]';\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'Button',\n"
                    "  file: 'frontend/src/components/ui/button.tsx',\n"
                    "  testFiles: ['frontend/tests/button.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/button',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-button'],\n"
                    "  tokenFamilies: ['radius'],\n"
                    "}];\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_rejects_radius_exemption_for_shadcn_contract_outside_ui(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/features/auth/components/FakePrimitive.tsx": (
                    "export const bad = 'rounded-xl rounded-[4px]';\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'FakePrimitive',\n"
                    "  file: 'frontend/src/features/auth/components/FakePrimitive.tsx',\n"
                    "  testFiles: ['frontend/tests/fake-primitive.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/fake-primitive',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['radius'],\n"
                    "}];\n"
                ),
            }
        )

        joined = "\n".join(issues)
        self.assertIn("avoid radius above 8px", joined)
        self.assertIn("use shared radius tokens", joined)

    def test_rejects_upstream_radius_in_axis_owned_component(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/components/shared/CustomControl.tsx": (
                    "export const bad = 'rounded-xl rounded-[4px]';\n"
                ),
            }
        )

        joined = "\n".join(issues)
        self.assertIn("avoid radius above 8px", joined)
        self.assertIn("use shared radius tokens", joined)


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

    def test_rejects_non_registry_case_ui_primitive_filename(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/CustomControl.tsx": "export function CustomControl() { return null; }\n",
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/CustomControl.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/custom-control',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("shadcn UI primitive files must use registry kebab-case names", "\n".join(issues))

    def test_rejects_non_pascal_case_shared_component_filename(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/action-link.tsx": "export function ActionLink() { return null; }\n",
                "frontend/src/components/shared/shell-nav.ts": "export const shellNavItems = [];\n",
            }
        )

        joined = "\n".join(issues)
        self.assertIn("shared React component files must use PascalCase names", joined)
        self.assertIn("shared non-component modules must use camelCase names", joined)

    def test_accepts_shared_component_filename_conventions(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/shared/ActionLink.tsx": "export function ActionLink() { return null; }\n",
                "frontend/src/components/shared/shellNav.ts": "export const shellNavItems = [];\n",
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

    def test_accepts_native_standard_controls_inside_shadcn_ui_primitives(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/button.tsx": (
                    "export function Button() {\n"
                    "  return <button type=\"button\"><input /></button>;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'Button',\n"
                    "  file: 'frontend/src/components/ui/button.tsx',\n"
                    "  testFiles: ['frontend/tests/button.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/button',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color'],\n"
                    "}];\n"
                ),
                "frontend/tests/button.test.tsx": "export {};\n",
            }
        )

        self.assertEqual([], issues)

    def test_rejects_ui_primitive_without_contract(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [];\n"
                ),
            }
        )

        self.assertIn("UI primitive must be listed", "\n".join(issues))

    def test_rejects_primitive_contract_without_required_readiness_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/custom-control.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("contract readiness must be `ready` or `candidate`", "\n".join(issues))
        self.assertIn("contract source must be `shadcn`", "\n".join(issues))
        self.assertIn("contract must list at least one variants value", "\n".join(issues))

    def test_rejects_primitive_contract_with_incomplete_source_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/custom-control.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("shadcn contract must name sourceItem `@shadcn/...`", "\n".join(issues))

    def test_rejects_primitive_contract_with_bare_shadcn_source_item(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/custom-control.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("shadcn contract must name sourceItem `@shadcn/...`", "\n".join(issues))

    def test_rejects_non_shadcn_source_in_ui_primitive_contract(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/custom-control.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "  source: 'axis',\n"
                    "  sourceReason: 'No shadcn equivalent for this test primitive.',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("contract source must be `shadcn`", "\n".join(issues))

    def test_rejects_unknown_primitive_contract_token_family(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/components/ui/custom-control.tsx": (
                    "export function CustomControl() {\n"
                    "  return <button type=\"button\" />;\n"
                    "}\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'CustomControl',\n"
                    "  file: 'frontend/src/components/ui/custom-control.tsx',\n"
                    "  testFiles: ['frontend/tests/custom-control.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/custom-control',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['raw-shadow'],\n"
                    "}];\n"
                ),
                "frontend/tests/custom-control.test.tsx": "export {};\n",
            }
        )

        self.assertIn("unknown tokenFamilies values: `raw-shadow`", "\n".join(issues))

    def test_rejects_route_bound_consumer_without_contract_registry(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing/components/LandingPage';\n"
                    "export const Route = createLazyFileRoute('/')({ component: LandingPage });\n"
                ),
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "export function LandingPage() { return null; }\n"
                ),
            }
        )

        self.assertIn("missing consumer contract registry", "\n".join(issues))

    def test_accepts_route_bound_consumer_contract(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing/components/LandingPage';\n"
                    "export const Route = createLazyFileRoute('/')({ component: LandingPage });\n"
                ),
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "export function LandingPage() { return null; }\n"
                ),
                "frontend/src/design-system/consumer-contracts.ts": (
                    "export const axisConsumerContracts = [{\n"
                    "  surface: 'Public landing',\n"
                    "  kind: 'public',\n"
                    "  route: '/',\n"
                    "  component: 'LandingPage',\n"
                    "  file: 'frontend/src/features/landing/components/LandingPage.tsx',\n"
                    "  owner: 'landing',\n"
                    "  readiness: 'ready',\n"
                    "  primitives: ['ActionLink'],\n"
                    "  states: ['default'],\n"
                    "  evidence: ['e2e-smoke'],\n"
                    "  testFiles: ['frontend/e2e/local-dev-smoke.pw.ts'],\n"
                    "}];\n"
                ),
                "frontend/e2e/local-dev-smoke.pw.ts": "export {};\n",
            }
        )

        self.assertEqual([], issues)

    def test_rejects_route_bound_consumer_missing_from_contract(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing/components/LandingPage';\n"
                    "export const Route = createLazyFileRoute('/')({ component: LandingPage });\n"
                ),
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "export function LandingPage() { return null; }\n"
                ),
                "frontend/src/design-system/consumer-contracts.ts": (
                    "export const axisConsumerContracts = [];\n"
                ),
            }
        )

        self.assertIn("route-bound UI consumer must be listed", "\n".join(issues))

    def test_rejects_barrel_route_consumer_missing_from_contract(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing';\n"
                    "export const Route = createLazyFileRoute('/')({ component: LandingPage });\n"
                ),
                "frontend/src/features/landing/index.ts": (
                    "export { LandingPage } from '@/features/landing/components/LandingPage';\n"
                ),
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "export function LandingPage() { return null; }\n"
                ),
                "frontend/src/design-system/consumer-contracts.ts": (
                    "export const axisConsumerContracts = [];\n"
                ),
            }
        )

        self.assertIn("route-bound UI consumer must be listed", "\n".join(issues))

    def test_rejects_consumer_contract_partial_route_match(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/register-callback.lazy.tsx": (
                    "import { RegisterPage } from '@/features/auth/components/RegisterPage';\n"
                    "export const Route = createLazyFileRoute('/register-callback')({ component: RegisterPage });\n"
                ),
                "frontend/src/features/auth/components/RegisterPage.tsx": (
                    "export function RegisterPage() { return null; }\n"
                ),
                "frontend/src/design-system/consumer-contracts.ts": (
                    "export const axisConsumerContracts = [{\n"
                    "  surface: 'Register',\n"
                    "  kind: 'auth',\n"
                    "  route: '/register',\n"
                    "  component: 'RegisterPage',\n"
                    "  file: 'frontend/src/features/auth/components/RegisterPage.tsx',\n"
                    "  owner: 'auth',\n"
                    "  readiness: 'ready',\n"
                    "  primitives: ['Button'],\n"
                    "  states: ['default'],\n"
                    "  evidence: ['unit-test'],\n"
                    "  testFiles: ['frontend/tests/register-page.test.tsx'],\n"
                    "}];\n"
                ),
                "frontend/tests/register-page.test.tsx": "export {};\n",
            }
        )

        self.assertIn("contract route `/register` is not declared", "\n".join(issues))

    def test_rejects_consumer_contract_without_required_metadata(self) -> None:
        issues = self.issues_for_frontend(
            {
                "frontend/src/routes/index.lazy.tsx": (
                    "import { LandingPage } from '@/features/landing/components/LandingPage';\n"
                    "export const Route = createLazyFileRoute('/')({ component: LandingPage });\n"
                ),
                "frontend/src/features/landing/components/LandingPage.tsx": (
                    "export function LandingPage() { return null; }\n"
                ),
                "frontend/src/design-system/consumer-contracts.ts": (
                    "export const axisConsumerContracts = [{\n"
                    "  surface: 'Public landing',\n"
                    "  kind: 'public',\n"
                    "  route: '/',\n"
                    "  component: 'LandingPage',\n"
                    "  file: 'frontend/src/features/landing/components/LandingPage.tsx',\n"
                    "  owner: 'landing',\n"
                    "  readiness: 'unknown',\n"
                    "  primitives: [],\n"
                    "  states: [],\n"
                    "  evidence: ['manual'],\n"
                    "  testFiles: ['frontend/e2e/missing.pw.ts'],\n"
                    "}];\n"
                ),
            }
        )

        joined = "\n".join(issues)
        self.assertIn("contract readiness must be `ready` or `candidate`", joined)
        self.assertIn("contract must list at least one primitives value", joined)
        self.assertIn("contract must list at least one states value", joined)
        self.assertIn("unknown evidence values: `manual`", joined)
        self.assertIn("test file `frontend/e2e/missing.pw.ts` does not exist", joined)

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


class TestFrontendDesignTokenUsage(unittest.TestCase):
    def issues_for_files(self, files: dict[str, str]) -> list[str]:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative_path, content in files.items():
                path = root / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            return axis.frontend_design_token_usage_issues(root=root)

    def issues_for_frontend(self, component_source: str) -> list[str]:
        return self.issues_for_files({"frontend/src/components/Example.tsx": component_source})

    def test_rejects_raw_neutral_color_utilities(self) -> None:
        issues = self.issues_for_frontend(
            "export const bad = 'bg-white text-slate-700 border-zinc-200';\n"
        )

        self.assertIn("use semantic color tokens", "\n".join(issues))

    def test_rejects_raw_shadow_utilities(self) -> None:
        issues = self.issues_for_frontend("export const bad = 'shadow-sm shadow-[0_1px_2px_red]';\n")

        self.assertIn("use named shadow tokens", "\n".join(issues))

    def test_rejects_bare_shadow_utility(self) -> None:
        issues = self.issues_for_frontend("export const bad = 'shadow';\n")

        self.assertIn("use named shadow tokens", "\n".join(issues))

    def test_rejects_arbitrary_color_and_gradient_values(self) -> None:
        issues = self.issues_for_frontend(
            "export const bad = 'bg-[linear-gradient(red,blue)] border-[hsl(var(--border))]';\n"
        )

        self.assertIn("move arbitrary color or gradient values into design-system tokens", "\n".join(issues))

    def test_accepts_upstream_design_classes_in_shadcn_sourced_primitive(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/components/ui/card.tsx": (
                    "export const upstream = 'bg-white text-slate-700 shadow-sm';\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'Card',\n"
                    "  file: 'frontend/src/components/ui/card.tsx',\n"
                    "  testFiles: ['frontend/tests/ui-primitives.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/card',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['semantic-caller-owned'],\n"
                    "  tokenFamilies: ['color', 'shadow'],\n"
                    "}];\n"
                ),
            }
        )

        self.assertEqual([], issues)

    def test_rejects_design_token_exemption_for_shadcn_contract_outside_ui(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/features/auth/components/FakePrimitive.tsx": (
                    "export const bad = 'bg-white text-slate-700 shadow-sm';\n"
                ),
                "frontend/src/design-system/primitive-contracts.ts": (
                    "export const axisPrimitiveContracts = [{\n"
                    "  component: 'FakePrimitive',\n"
                    "  file: 'frontend/src/features/auth/components/FakePrimitive.tsx',\n"
                    "  testFiles: ['frontend/tests/fake-primitive.test.tsx'],\n"
                    "  source: 'shadcn',\n"
                    "  sourceItem: '@shadcn/fake-primitive',\n"
                    "  readiness: 'ready',\n"
                    "  variants: ['default'],\n"
                    "  states: ['default'],\n"
                    "  accessibility: ['native-control'],\n"
                    "  tokenFamilies: ['color', 'shadow'],\n"
                    "}];\n"
                ),
            }
        )

        joined = "\n".join(issues)
        self.assertIn("use semantic color tokens", joined)
        self.assertIn("use named shadow tokens", joined)

    def test_rejects_raw_design_classes_in_axis_owned_component(self) -> None:
        issues = self.issues_for_files(
            {
                "frontend/src/components/shared/CustomCard.tsx": (
                    "export const bad = 'bg-white text-slate-700 shadow-sm';\n"
                ),
            }
        )

        joined = "\n".join(issues)
        self.assertIn("use semantic color tokens", joined)
        self.assertIn("use named shadow tokens", joined)

    def test_accepts_semantic_token_utilities(self) -> None:
        issues = self.issues_for_frontend(
            "export const ok = 'bg-card text-card-foreground border-border shadow-surface shadow-control shadow-accent-control shadow-panel shadow-feature-panel bg-gradient-inverse-panel';\n"
        )

        self.assertEqual([], issues)

    def test_accepts_shadow_token_variable_names(self) -> None:
        issues = self.issues_for_frontend(
            "export const tokens = ['--action-accent-shadow', '--shadow-surface'];\n"
        )

        self.assertEqual([], issues)

    def test_accepts_primitive_contract_shadow_token_family_metadata(self) -> None:
        issues = self.issues_for_frontend(
            "export const axisPrimitiveContracts = [{ tokenFamilies: ['color', 'shadow'] }];\n"
        )

        self.assertEqual([], issues)

    def test_accepts_token_family_type_literal(self) -> None:
        issues = self.issues_for_frontend(
            "export type AxisPrimitiveTokenFamily =\n"
            "  | 'color'\n"
            "  | 'shadow'\n"
            "  | 'radius';\n"
        )

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
            files[workflow] = files[workflow].replace("run: python scripts/axis.py check doc-drift\n", "")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.write_truth_repo(root, mutate)
            issues = axis.enforcement_truth_audit_issues(root=root)

        self.assertIn("doc drift runs in CI", "\n".join(issues))

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
            script.chmod(0o755)

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
            hook.chmod(0o755)

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
    def run_local_dev(self, args: axis.argparse.Namespace) -> list[list[str]]:
        calls: list[list[str]] = []

        def fake_run(command: list[str], **_kwargs):
            calls.append(command)
            return axis.subprocess.CompletedProcess(command, 0, stdout="", stderr="")

        with (
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

    def test_e2e_builds_and_runs_profile(self) -> None:
        calls = self.run_local_dev(axis.argparse.Namespace(local_dev_command="e2e"))

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
            mock.patch.object(axis, "_docker_compose_ok", return_value=True),
            mock.patch.object(axis, "run", side_effect=fake_run),
        ):
            self.assertEqual(0, axis.local_dev(axis.argparse.Namespace(local_dev_command="reset-db")))

        self.assertEqual(["compose", "-p", "axis", "-f", str(axis.LOCAL_DEV_COMPOSE_FILE), "up", "-d"], calls[2][1:])


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

    def test_dotnet_build_strips_argparse_separator(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="build", dotnet_args=["--", "--no-restore"]),
        )

        self.assertEqual(["dotnet", "build", "Axis.sln", "--nologo", "--no-restore"], calls[0])

    def test_dotnet_format_check_uses_verify_no_changes(self) -> None:
        calls = self.run_with_fake_process(
            axis.dotnet_command,
            axis.argparse.Namespace(dotnet_command="format", check=True, dotnet_args=[]),
        )

        self.assertEqual(["dotnet", "format", "Axis.sln", "--verify-no-changes"], calls[0])

    def test_frontend_gen_api_types_check_diffs_generated_file(self) -> None:
        calls = self.run_with_fake_process(
            axis.frontend_command,
            axis.argparse.Namespace(frontend_command="gen-api-types", check=True),
        )

        self.assertEqual(["npm", "run", "gen:api-types"], calls[0])
        self.assertEqual(["git", "diff", "--exit-code", "--", "frontend/src/lib/api-types.ts"], calls[1])

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
                mock.patch.object(axis, "exe", side_effect=lambda name: name),
                mock.patch.object(axis.shutil, "which", return_value="/usr/bin/openssl"),
                mock.patch.object(axis.Path, "chmod", autospec=True) as chmod,
                contextlib.redirect_stdout(io.StringIO()),
            ):
                self.assertEqual(0, axis.local_dev_certs())

            self.assertTrue((cert_dir / "localhost.ext").is_file())
            self.assertIn("subjectAltName=@alt_names", (cert_dir / "localhost.ext").read_text(encoding="utf-8"))
            chmod.assert_any_call(cert_dir, 0o700)
            chmod.assert_any_call(cert_dir / "rootCA-key.pem", 0o600)
            chmod.assert_any_call(cert_dir / "localhost-key.pem", 0o600)
            self.assertEqual("openssl", calls[0][0])


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
