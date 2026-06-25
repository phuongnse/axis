#!/usr/bin/env python3
"""Validate flow-first use-case docs structure.

Usage:
  python3 scripts/check-use-case-docs.py --check
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = set()

REQUIRED_SECTIONS = (
    "Purpose",
    "Primary actor",
    "Trigger",
    "Main flow",
    "Alternate / error flows",
    "Acceptance Criteria",
    "Acceptance Test Matrix",
    "Out Of Scope",
    "Design System",
    "Design Sources",
)
IMPLEMENTATION_STATUS_HEADING = "> **Implementation status**"
IMPLEMENTATION_STATUS_REQUIRED_MARKERS = (
    ("> **Gaps vs spec:**", "gaps vs spec"),
    ("> **Deferred follow-ups:**", "deferred follow-ups"),
    ("> **Verification:**", "verification"),
    ("> **Decisions:**", "decisions"),
)
IMPLEMENTATION_STATUS_VALUES = {"Done", "Partial", "Not started", "N/A"}
PENDING_STATUS_VALUES = {"Partial", "Not started"}
GENERIC_STATUS_PROSE = (
    "Open work remains in layers marked pending above.",
    "Complete the open items listed in Gaps vs spec before marking this use case complete.",
)

AC_ID_RE = re.compile(r"\bAC-\d{3}\b")
AT_ID_RE = re.compile(r"^AT-\d{3}$")
AC_BULLET_RE = re.compile(r"^\s*-\s+(?P<body>.+)$", re.MULTILINE)
AC_BOLD_ID_PREFIX_RE = re.compile(r"^\*\*(AC-\d{3})\*\*\s+\S")
H2_RE = re.compile(r"^##\s+(.+?)\s*$", re.MULTILINE)
IMPLEMENTATION_DETAIL_MATRIX_RE = re.compile(
    r"(?:`?(?:frontend|src|tests|scripts|[.]agents)/[^`|\s]+`?)|"
    r"(?:\bAxis[.][A-Za-z0-9_.]*Tests\b)|"
    r"(?:\b[A-Za-z0-9_.]+[.](?:cs|tsx?|jsx?|mjs|py)\b)|"
    r"(?:`?(?:dotnet|npm|npx|pnpm|yarn|python|docker(?:\s+compose)?)\b[^`|]*)"
)
ACCEPTANCE_MATRIX_COLUMNS = [
    "ID",
    "Level",
    "Scenario",
    "Covers AC",
    "Automated by",
    "Required to close",
]
ACCEPTANCE_MATRIX_LEVELS = {
    "E2E",
    "API",
    "Application",
    "Infrastructure",
    "Domain",
    "Component",
    "UI",
}
AUTOMATED_BY_VALUES = {
    "Playwright",
    "Vitest",
    "xUnit",
    "xUnit API",
    "xUnit Application",
    "xUnit Infrastructure",
    "xUnit Domain",
    "xUnit Architecture",
}
IMPLEMENTATION_STATUS_COLUMNS = ["Layer", "Status"]
DESIGN_SOURCE_REQUIRED_COLUMNS = {"Screen", "Preview"}
DESIGN_SOURCE_SOURCE_COLUMNS = {"Source"}
NO_ARTIFACT_VALUES = {"", "-", "—", "n/a", "na", "none"}
PREVIEW_ASSET_RE = re.compile(r"[.](?:svg|png|jpe?g|gif|webp|avif)(?:[)#?]|$)", re.IGNORECASE)
USE_CASE_TAIL_SECTION_ORDER = (
    "Out Of Scope",
    "Design System",
    "Screen flow",
    "Design Sources",
    "Diagrams",
    "Implementation status",
)

# Placeholder markers that must be replaced before a use case can be considered
# written. Listed individually so the validator can point at the missing field.
PURPOSE_PLACEHOLDERS = (
    "_(One sentence about user value.)_",
    "<One sentence about user value.>",
)
ACTOR_PLACEHOLDERS = (
    "- _(Actor)_",
    "- <actor>",
)
TRIGGER_PLACEHOLDERS = (
    "- _(What starts the use case.)_",
    "- <what starts the use case>",
)
# The 3-line stock Main flow shipped by the migration. If a use case still has
# this verbatim, no real flow has been written yet.
TEMPLATE_MAIN_FLOW = (
    "1. Actor satisfies the trigger.\n"
    "2. System performs the happy-path steps in Acceptance Criteria.\n"
    "3. Actor receives the expected outcome."
)


@dataclass(frozen=True)
class MarkdownTable:
    headers: list[str]
    rows: list[list[str]]
    start_line: int


@dataclass(frozen=True)
class UseCaseDocument:
    path: Path
    root: Path
    text: str
    sections: dict[str, str]
    h2_headings: list[str]

    @property
    def rel(self) -> Path:
        return self.path.relative_to(self.root)

    def section(self, heading: str) -> str:
        return self.sections.get(heading, "")


def split_h2_sections(text: str) -> tuple[list[str], dict[str, str]]:
    matches = list(H2_RE.finditer(text))
    headings: list[str] = [match.group(1).strip() for match in matches]
    sections: dict[str, str] = {}
    for idx, match in enumerate(matches):
        start = match.end()
        end = matches[idx + 1].start() if idx + 1 < len(matches) else len(text)
        sections.setdefault(match.group(1).strip(), text[start:end].strip("\n"))
    return headings, sections


def use_case_document(path: Path) -> UseCaseDocument:
    text = path.read_text(encoding="utf-8")
    headings, sections = split_h2_sections(text)
    return UseCaseDocument(path=path, root=ROOT, text=text, sections=sections, h2_headings=headings)


def parse_table_row(line: str) -> list[str] | None:
    stripped = line.strip()
    if not stripped.startswith("|") or not stripped.endswith("|"):
        return None
    return [cell.strip() for cell in stripped.strip("|").split("|")]


def is_table_separator(cells: list[str]) -> bool:
    return bool(cells) and all(re.fullmatch(r":?-{3,}:?", cell.replace(" ", "")) for cell in cells)


def first_markdown_table(section: str) -> MarkdownTable | None:
    lines = section.splitlines()
    for idx in range(len(lines) - 1):
        headers = parse_table_row(lines[idx])
        separator = parse_table_row(lines[idx + 1])
        if headers is None or separator is None or not is_table_separator(separator):
            continue

        rows: list[list[str]] = []
        for row_line in lines[idx + 2 :]:
            parsed = parse_table_row(row_line)
            if parsed is None:
                break
            rows.append(parsed)
        return MarkdownTable(headers=headers, rows=rows, start_line=idx + 1)
    return None


def validate_table_shape(
    table: MarkdownTable | None,
    *,
    rel: Path,
    label: str,
    exact_columns: list[str] | None = None,
    required_columns: set[str] | None = None,
) -> list[str]:
    if table is None:
        return [f"{rel}: {label} must contain a markdown table"]

    issues: list[str] = []
    if len(set(table.headers)) != len(table.headers):
        issues.append(f"{rel}: {label} table has duplicate columns")
    if exact_columns is not None and table.headers != exact_columns:
        issues.append(
            f"{rel}: {label} table columns must be exactly `{' | '.join(exact_columns)}`",
        )
    if required_columns is not None:
        missing = sorted(required_columns.difference(table.headers))
        if missing:
            issues.append(f"{rel}: {label} table missing required columns: {', '.join(missing)}")
    for idx, row in enumerate(table.rows, start=1):
        if len(row) != len(table.headers):
            issues.append(
                f"{rel}: {label} table row {idx} has {len(row)} cells but header has {len(table.headers)}",
            )
    if not table.rows:
        issues.append(f"{rel}: {label} table must have at least one row")
    return issues


def record_for_row(table: MarkdownTable, row: list[str]) -> dict[str, str]:
    return {header: row[idx] if idx < len(row) else "" for idx, header in enumerate(table.headers)}


def is_no_artifact_cell(cell: str) -> bool:
    return cell.strip().lower() in NO_ARTIFACT_VALUES


def is_preview_asset_reference(cell: str) -> bool:
    return PREVIEW_ASSET_RE.search(cell) is not None


def is_editable_design_source(cell: str) -> bool:
    return not is_no_artifact_cell(cell) and not is_preview_asset_reference(cell)


def acceptance_criteria_ids(section: str) -> tuple[set[str], list[str], list[str], list[str]]:
    ids: list[str] = []
    missing_id_bullets: list[str] = []
    invalid_format_bullets: list[str] = []
    for match in AC_BULLET_RE.finditer(section):
        body = match.group("body").strip()

        found = AC_ID_RE.search(body)
        if found:
            ids.append(found.group(0))
            if not AC_BOLD_ID_PREFIX_RE.match(body):
                invalid_format_bullets.append(body)
        else:
            missing_id_bullets.append(body)
    duplicate_ids = sorted({ac_id for ac_id in ids if ids.count(ac_id) > 1})
    return set(ids), duplicate_ids, missing_id_bullets, invalid_format_bullets


def implementation_status_callout(text: str) -> str:
    lines = text.splitlines()
    for idx, line in enumerate(lines):
        if line != IMPLEMENTATION_STATUS_HEADING:
            continue
        block: list[str] = []
        for block_line in lines[idx:]:
            if not block_line.startswith(">"):
                break
            block.append(block_line)
        return "\n".join(block)
    return ""


def callout_has_pending_layer(table: MarkdownTable | None) -> bool:
    if table is None:
        return False
    return any(
        record_for_row(table, row).get("Status", "") in PENDING_STATUS_VALUES
        for row in table.rows
    )


def callout_section_content(callout: str, marker: str) -> list[str]:
    lines = callout.splitlines()
    for idx, line in enumerate(lines):
        if not line.startswith(marker):
            continue
        content: list[str] = []
        inline = line.removeprefix(marker).strip()
        if inline:
            content.append(inline)
        for next_line in lines[idx + 1 :]:
            if next_line.startswith("> **") and ":**" in next_line:
                break
            if next_line.startswith("> |"):
                break
            stripped = next_line.removeprefix(">").strip()
            if stripped:
                content.append(stripped)
        return content
    return []


def iter_use_case_files() -> list[Path]:
    files: list[Path] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        for readme in sorted(domain_dir.glob("*/README.md")):
            if readme.parent.name in SKIP_DIRS:
                continue
            files.append(readme)
    return files


def validate_sections(doc: UseCaseDocument) -> list[str]:
    issues: list[str] = []
    rel = doc.rel

    duplicates = sorted({heading for heading in doc.h2_headings if doc.h2_headings.count(heading) > 1})
    for heading in duplicates:
        issues.append(f"{rel}: duplicate `## {heading}` section")

    for heading in REQUIRED_SECTIONS:
        if heading == "Acceptance Test Matrix":
            continue
        if heading not in doc.sections:
            issues.append(f"{rel}: missing {heading.lower()} section")

    for placeholder in PURPOSE_PLACEHOLDERS:
        if placeholder in doc.text:
            issues.append(f"{rel}: Purpose still has placeholder `{placeholder}`")
    for placeholder in ACTOR_PLACEHOLDERS:
        if placeholder in doc.text:
            issues.append(f"{rel}: Primary actor still has placeholder `{placeholder.strip()}`")
    for placeholder in TRIGGER_PLACEHOLDERS:
        if placeholder in doc.text:
            issues.append(f"{rel}: Trigger still has placeholder `{placeholder.strip()}`")

    return issues


def section_position(doc: UseCaseDocument, heading: str) -> int | None:
    if heading == "Implementation status":
        idx = doc.text.find(IMPLEMENTATION_STATUS_HEADING)
        return idx if idx >= 0 else None

    match = re.search(rf"^##\s+{re.escape(heading)}\s*$", doc.text, re.MULTILINE)
    return match.start() if match else None


def validate_section_order(doc: UseCaseDocument) -> list[str]:
    positions = [
        (heading, position)
        for heading in USE_CASE_TAIL_SECTION_ORDER
        if (position := section_position(doc, heading)) is not None
    ]
    for (previous_heading, previous_position), (current_heading, current_position) in zip(
        positions,
        positions[1:],
        strict=False,
    ):
        if previous_position <= current_position:
            continue
        return [
            f"{doc.rel}: section order must be `## Out Of Scope`, `## Design System`, "
            "`## Screen flow` when present, `## Design Sources`, `## Diagrams` when present, "
            "then implementation status "
            f"(`{current_heading}` appears before `{previous_heading}`)",
        ]
    return []


def validate_acceptance_contract(doc: UseCaseDocument, *, require_matrix: bool) -> list[str]:
    issues: list[str] = []
    rel = doc.rel
    ac_section = doc.section("Acceptance Criteria")
    (
        ac_ids,
        duplicate_ac_ids,
        missing_id_bullets,
        invalid_format_bullets,
    ) = acceptance_criteria_ids(ac_section)
    for ac_id in duplicate_ac_ids:
        issues.append(f"{rel}: duplicate acceptance criterion ID `{ac_id}`")
    for bullet in invalid_format_bullets:
        issues.append(
            f"{rel}: Acceptance Criteria bullet must use `- **AC-001** ...` format: {bullet}",
        )

    if "Acceptance Test Matrix" not in doc.sections:
        if require_matrix:
            issues.append(f"{rel}: missing acceptance test matrix section")
        return issues

    matrix_section = doc.section("Acceptance Test Matrix")
    if not matrix_section.strip():
        issues.append(f"{rel}: Acceptance Test Matrix section is empty")
        return issues

    for bullet in missing_id_bullets:
        issues.append(
            f"{rel}: Acceptance Criteria bullet lacks an AC ID while an Acceptance Test Matrix exists: {bullet}",
        )
    if not ac_ids:
        issues.append(f"{rel}: Acceptance Test Matrix exists but no AC IDs were found")

    table = first_markdown_table(matrix_section)
    issues.extend(
        validate_table_shape(
            table,
            rel=rel,
            label="Acceptance Test Matrix",
            exact_columns=ACCEPTANCE_MATRIX_COLUMNS,
        )
    )
    if table is None:
        return issues
    if "Evidence source" in table.headers:
        issues.append(
            f"{rel}: Acceptance Test Matrix must not include an `Evidence source` column; "
            "put spec citations in the implementation/verification report",
        )

    seen_at_ids: set[str] = set()
    id_prefixes: set[str] = set()
    required_coverage: set[str] = set()
    for idx, row in enumerate(table.rows, start=1):
        record = record_for_row(table, row)
        row_label = record.get("ID") or f"row {idx}"
        for cell in row:
            if IMPLEMENTATION_DETAIL_MATRIX_RE.search(cell):
                issues.append(
                    f"{rel}: Acceptance Test Matrix {row_label} contains implementation details; "
                    "use runner/tool names only, not file paths, class names, or commands",
                )

        at_id = record.get("ID", "")
        if not AT_ID_RE.fullmatch(at_id):
            issues.append(f"{rel}: Acceptance Test Matrix row {idx} has invalid ID `{at_id}`")
        elif at_id in seen_at_ids:
            issues.append(f"{rel}: duplicate Acceptance Test Matrix ID `{at_id}`")
        elif "-" in at_id:
            seen_at_ids.add(at_id)
            id_prefixes.add(at_id.split("-", 1)[0])

        level = record.get("Level", "")
        level_parts = [part.strip() for part in level.split("/") if part.strip()]
        if not level_parts or any(part not in ACCEPTANCE_MATRIX_LEVELS for part in level_parts):
            issues.append(
                f"{rel}: Acceptance Test Matrix {row_label} has invalid Level `{level}`",
            )

        automated_by = record.get("Automated by", "")
        automation_parts = [part.strip() for part in automated_by.split("+") if part.strip()]
        if not automation_parts or any(part not in AUTOMATED_BY_VALUES for part in automation_parts):
            issues.append(
                f"{rel}: Acceptance Test Matrix {row_label} has invalid Automated by `{automated_by}`",
            )

        required = record.get("Required to close", "")
        if required not in {"Yes", "No"}:
            issues.append(
                f"{rel}: Acceptance Test Matrix {row_label} Required to close must be `Yes` or `No`",
            )

        covers = set(AC_ID_RE.findall(record.get("Covers AC", "")))
        if not covers:
            issues.append(f"{rel}: Acceptance Test Matrix {row_label} must cover at least one AC ID")
        unknown = sorted(covers.difference(ac_ids))
        if unknown:
            issues.append(
                f"{rel}: Acceptance Test Matrix {row_label} references unknown AC IDs: {', '.join(unknown)}",
            )
        if required == "Yes":
            required_coverage.update(covers)

    if len(id_prefixes) > 1:
        issues.append(
            f"{rel}: Acceptance Test Matrix IDs must use one local prefix, found: {', '.join(sorted(id_prefixes))}",
        )
    missing_required_coverage = sorted(ac_ids.difference(required_coverage))
    if missing_required_coverage:
        issues.append(
            f"{rel}: Acceptance Test Matrix required rows do not cover AC IDs: {', '.join(missing_required_coverage)}",
        )

    return issues


def validate_design_sources(doc: UseCaseDocument) -> list[str]:
    table = first_markdown_table(doc.section("Design Sources"))
    issues = validate_table_shape(
        table,
        rel=doc.rel,
        label="Design Sources",
        required_columns=DESIGN_SOURCE_REQUIRED_COLUMNS,
    )
    if table is not None and DESIGN_SOURCE_SOURCE_COLUMNS.isdisjoint(table.headers):
        issues.append(
            f"{doc.rel}: Design Sources table missing required columns: Source",
        )
    if table is None:
        return issues
    source_headers = [header for header in ("Source",) if header in table.headers]
    for idx, row in enumerate(table.rows, start=1):
        record = record_for_row(table, row)
        screen = record.get("Screen", "").strip() or f"row {idx}"
        source_cells = [record.get(header, "") for header in source_headers]
        preview_cell = record.get("Preview", "")

        for header, source_cell in zip(source_headers, source_cells, strict=False):
            if is_preview_asset_reference(source_cell):
                issues.append(
                    f"{doc.rel}: Design Sources `{screen}` {header} must link to an editable design source, "
                    "not a preview/export asset",
                )

        if not is_no_artifact_cell(preview_cell) and not any(is_editable_design_source(cell) for cell in source_cells):
            issues.append(
                f"{doc.rel}: Design Sources `{screen}` has a preview but no editable Source",
            )
    return issues


def validate_design_system(doc: UseCaseDocument) -> list[str]:
    table = first_markdown_table(doc.section("Design System"))
    return validate_table_shape(
        table,
        rel=doc.rel,
        label="Design System",
        exact_columns=["Surface", "Contract"],
    )


def validate_diagrams(doc: UseCaseDocument) -> list[str]:
    issues: list[str] = []
    rel = doc.rel
    diagrams_section = doc.section("Diagrams")
    if diagrams_section and "```mermaid" not in diagrams_section:
        issues.append(
            f"{rel}: ## Diagrams must contain at least one ```mermaid block "
            "(or omit ## Diagrams when there is no local diagram)",
        )
    return issues


def validate_implementation_status(doc: UseCaseDocument, *, strict_status: bool) -> list[str]:
    issues: list[str] = []
    rel = doc.rel

    callout = implementation_status_callout(doc.text)
    if not callout:
        issues.append(f"{rel}: missing implementation status callout")
        return issues

    status_table = first_markdown_table(
        "\n".join(line.removeprefix(">").strip() for line in callout.splitlines())
    )
    issues.extend(
        validate_table_shape(
            status_table,
            rel=rel,
            label="Implementation status",
            exact_columns=IMPLEMENTATION_STATUS_COLUMNS,
        )
    )
    if status_table is not None:
        for idx, row in enumerate(status_table.rows, start=1):
            status = record_for_row(status_table, row).get("Status", "")
            if status not in IMPLEMENTATION_STATUS_VALUES:
                issues.append(
                    f"{rel}: Implementation status row {idx} has invalid Status `{status}`; "
                    f"use {', '.join(sorted(IMPLEMENTATION_STATUS_VALUES))}",
                )

    if strict_status:
        for marker, label in IMPLEMENTATION_STATUS_REQUIRED_MARKERS:
            if marker not in callout:
                issues.append(f"{rel}: missing implementation status {label} section")
        for marker, label in IMPLEMENTATION_STATUS_REQUIRED_MARKERS:
            content = callout_section_content(callout, marker)
            if not content:
                issues.append(f"{rel}: implementation status {label} section is empty")
            if any(generic in line for generic in GENERIC_STATUS_PROSE for line in content):
                issues.append(f"{rel}: implementation status {label} uses generic placeholder prose")
        if callout_has_pending_layer(status_table):
            if any(
                line in {
                    "> **Gaps vs spec:** none",
                    "> **Gaps vs spec:** none.",
                    "> **Gaps vs spec:** none for the current documented scope.",
                }
                for line in callout.splitlines()
            ):
                issues.append(f"{rel}: pending layer cannot use `Gaps vs spec: none`")

    return issues


def check_file(
    path: Path,
    *,
    strict_status: bool = True,
    require_acceptance_matrix: bool = True,
) -> list[str]:
    doc = use_case_document(path)
    issues: list[str] = []
    issues.extend(validate_sections(doc))
    issues.extend(validate_section_order(doc))
    issues.extend(validate_acceptance_contract(doc, require_matrix=require_acceptance_matrix))
    issues.extend(validate_design_system(doc))
    issues.extend(validate_design_sources(doc))
    issues.extend(validate_diagrams(doc))
    issues.extend(validate_implementation_status(doc, strict_status=strict_status))
    return issues


def count_template_main_flow(files: list[Path]) -> int:
    return sum(1 for p in files if TEMPLATE_MAIN_FLOW in p.read_text(encoding="utf-8"))


def diff_range_against_base() -> str:
    """Return the PR-style git range; fail closed without diff context."""
    base = os.environ.get("BASE_BRANCH", "main")
    candidates = [f"origin/{base}", base]
    range_spec: str | None = None

    for candidate in candidates:
        result = subprocess.run(
            ["git", "rev-parse", "--verify", candidate],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if result.returncode == 0:
            range_spec = f"{candidate}...HEAD"
            break

    if range_spec is None:
        result = subprocess.run(
            ["git", "rev-parse", "--verify", "HEAD~1"],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if result.returncode == 0:
            range_spec = "HEAD~1...HEAD"

    if range_spec is None:
        raise RuntimeError(
            "check-use-case-docs: failed to find a git diff base "
            f"(tried origin/{base}, {base}, and HEAD~1)"
        )

    return range_spec


def changed_paths_against_base() -> list[Path]:
    """Return PR-range and working-tree paths for local and CI checks."""
    range_spec = diff_range_against_base()

    paths: list[Path] = []
    seen: set[Path] = set()

    def collect(args: list[str], label: str) -> None:
        result = subprocess.run(
            ["git", *args],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if result.returncode != 0:
            raise RuntimeError(f"check-use-case-docs: failed to diff {label}")
        for line in result.stdout.splitlines():
            if not line.strip():
                continue
            path = (ROOT / line.strip()).resolve()
            if path in seen:
                continue
            seen.add(path)
            paths.append(path)

    collect(["diff", "--name-only", range_spec], range_spec)
    collect(["diff", "--name-only", "--cached"], "staged changes")
    collect(["diff", "--name-only"], "unstaged changes")
    collect(["ls-files", "--others", "--exclude-standard"], "untracked files")
    return paths


def strip_implementation_status_callouts(text: str) -> str:
    """Remove status callout blocks so checks can distinguish status-only edits."""
    lines = text.splitlines()
    out: list[str] = []
    idx = 0
    while idx < len(lines):
        if lines[idx] == IMPLEMENTATION_STATUS_HEADING:
            idx += 1
            while idx < len(lines) and lines[idx].startswith(">"):
                idx += 1
            continue
        out.append(lines[idx])
        idx += 1
    return "\n".join(out).strip()


def material_change_snapshot(text: str) -> str:
    normalized = strip_implementation_status_callouts(text)
    return re.sub(r"\n{3,}", "\n\n", normalized).strip()


def content_snapshot_ref(range_spec: str) -> str:
    if "..." not in range_spec:
        return range_spec

    left_ref, right_ref = range_spec.split("...", 1)
    result = subprocess.run(
        ["git", "merge-base", left_ref, right_ref],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    if result.returncode == 0 and result.stdout.strip():
        return result.stdout.strip()
    return left_ref


def changed_use_case_content_outside_status(path: Path, range_spec: str) -> bool:
    base_ref = content_snapshot_ref(range_spec)
    relative = path.relative_to(ROOT).as_posix()
    result = subprocess.run(
        ["git", "show", f"{base_ref}:{relative}"],
        cwd=ROOT,
        encoding="utf-8",
        errors="replace",
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    previous = result.stdout if result.returncode == 0 else ""
    current = path.read_text(encoding="utf-8")
    return material_change_snapshot(previous) != material_change_snapshot(current)


def check_changed_stock_main_flow(files: list[Path]) -> list[str]:
    """Ratchet: existing stock flows are debt; content edits must replace them."""
    range_spec = diff_range_against_base()
    changed = set(changed_paths_against_base())
    if not changed:
        return []

    issues: list[str] = []
    for path in files:
        resolved = path.resolve()
        if resolved not in changed:
            continue
        if TEMPLATE_MAIN_FLOW not in path.read_text(encoding="utf-8"):
            continue
        if not changed_use_case_content_outside_status(path, range_spec):
            continue
        rel = path.relative_to(ROOT)
        issues.append(
            f"{rel}: changed use-case content still has the stock Main flow — replace it with the real user/system flow"
        )
    return issues


# Row in a domain README that references `./README.md` from inside that same
# README is a self-link — it resolves to the current page and conveys
# nothing.
SELF_LINK_RE = re.compile(r"\]\(\./README\.md(?:#[^)]*)?\)")

# Suspected truncation in a `| ... | summary |` row: the summary cell ends
# without sentence-terminating punctuation and is roughly the length of the
# old hard 100-char cut. Tolerates legit short cells (≤80 chars), legit
# punctuation, and lines that are not table rows.
TABLE_ROW_RE = re.compile(r"^\|[^|]+\|\s*([^|]+?)\s*\|\s*$", re.MULTILINE)
TRUNCATION_TERMINAL = (".", "?", "!", "…", ":")


def check_domain_readme(path: Path) -> list[str]:
    """Domain README-only checks (self-links + truncated table rows)."""
    issues: list[str] = []
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT)

    for match in SELF_LINK_RE.finditer(text):
        line_no = text.count("\n", 0, match.start()) + 1
        issues.append(
            f"{rel}:{line_no}: self-link to ./README.md — replace with a real use-case slug or drop the link"
        )

    in_use_cases_section = False
    for idx, line in enumerate(text.splitlines(), start=1):
        stripped = line.strip()
        if stripped.startswith("## Use Cases") or stripped.startswith("### "):
            in_use_cases_section = stripped == "## Use Cases" or in_use_cases_section
        if stripped.startswith("## ") and not stripped.startswith("## Use Cases"):
            in_use_cases_section = False
        if not in_use_cases_section:
            continue
        match = TABLE_ROW_RE.match(line)
        if not match:
            continue
        summary = match.group(1).strip()
        if len(summary) < 80:
            continue
        if summary.endswith(TRUNCATION_TERMINAL):
            continue
        issues.append(
            f"{rel}:{idx}: suspected truncated summary row (no terminal punctuation, {len(summary)} chars): {summary[-40:]!r}"
        )

    return issues


def iter_domain_readmes() -> list[Path]:
    readmes: list[Path] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        readme = domain_dir / "README.md"
        if readme.is_file():
            readmes.append(readme)
    return readmes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate and exit non-zero on issues")
    _ = parser.parse_args()

    files = iter_use_case_files()
    issues: list[str] = []
    changed_paths: set[Path] = set()
    try:
        changed_paths = set(changed_paths_against_base())
    except RuntimeError as exc:
        issues.append(str(exc))
    range_spec: str | None = None
    try:
        range_spec = diff_range_against_base()
    except RuntimeError as exc:
        issues.append(str(exc))

    for path in files:
        changed = path.resolve() in changed_paths
        requires_matrix = False
        if changed and range_spec is not None:
            requires_matrix = changed_use_case_content_outside_status(path, range_spec)
        issues.extend(
            check_file(
                path,
                strict_status=changed,
                require_acceptance_matrix=requires_matrix,
            )
        )
    try:
        issues.extend(check_changed_stock_main_flow(files))
    except RuntimeError as exc:
        issues.append(str(exc))
    for readme in iter_domain_readmes():
        issues.extend(check_domain_readme(readme))

    if issues:
        print("Use-case docs validation failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    # Soft signal: number of use cases that still ship the stock Main flow.
    # Tracked here so the debt is visible without blocking the build.
    stub_count = count_template_main_flow(files)
    if stub_count:
        print(
            f"check-use-case-docs: OK ({stub_count} of {len(files)} files still have the stock Main flow)"
        )
    else:
        print("check-use-case-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
