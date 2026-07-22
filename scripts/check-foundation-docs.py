#!/usr/bin/env python3
"""Validate foundation docs structure.

Usage:
  python3 scripts/check-foundation-docs.py --check
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

from acceptance_evidence import EvidenceValidationContext
from acceptance_evidence import evidence_file_for
from acceptance_evidence import validate_acceptance_evidence_sidecar

ROOT = Path(__file__).resolve().parent.parent
FOUNDATIONS = ROOT / "docs" / "foundations"

REQUIRED_SECTIONS = (
    "Purpose",
    "Primary actor",
    "Trigger",
    "Main flow",
    "Alternate / error flows",
    "Acceptance Criteria",
    "Acceptance Test Matrix",
    "Out Of Scope",
)
IMPLEMENTATION_STATUS_HEADING = "> **Implementation status**"
IMPLEMENTATION_STATUS_REQUIRED_MARKERS = (
    ("> **Gaps vs spec:**", "gaps vs spec"),
    ("> **Deferred follow-ups:**", "deferred follow-ups"),
    ("> **Verification:**", "verification"),
    ("> **Decisions:**", "decisions"),
)
IMPLEMENTATION_STATUS_VALUES = {"Done", "Partial", "Not started", "N/A"}
ACCEPTANCE_MATRIX_COLUMNS = ["ID", "Boundary", "Scenario", "Covers AC", "Verification", "Required"]
ACCEPTANCE_MATRIX_BOUNDARIES = {
    "Browser journey",
    "UI component",
    "Static frontend",
    "Layout smoke",
}
VERIFICATION_VALUES = {
    "Browser automation",
    "UI component test",
    "Frontend CI",
    "Browser-capable visual smoke",
}
IMPLEMENTATION_STATUS_COLUMNS = ["Layer", "Status"]
TAIL_SECTION_ORDER = (
    "Acceptance Test Matrix",
    "Out Of Scope",
    "Screen flow",
    "Diagrams",
    "Implementation status",
)

H2_RE = re.compile(r"^##\s+(.+?)\s*$", re.MULTILINE)
AC_ID_RE = re.compile(r"\bAC-\d{3}\b")
AT_ID_RE = re.compile(r"^AT-\d{3}$")
AC_BULLET_RE = re.compile(r"^\s*-\s+(?P<body>.+)$", re.MULTILINE)
AC_BOLD_ID_PREFIX_RE = re.compile(r"^\*\*(AC-\d{3})\*\*\s+\S")


@dataclass(frozen=True)
class MarkdownTable:
    headers: list[str]
    rows: list[list[str]]


@dataclass(frozen=True)
class FoundationDocument:
    path: Path
    text: str
    sections: dict[str, str]
    h2_headings: list[str]

    @property
    def rel(self) -> Path:
        return self.path.relative_to(ROOT)

    def section(self, heading: str) -> str:
        return self.sections.get(heading, "")


def split_h2_sections(text: str) -> tuple[list[str], dict[str, str]]:
    matches = list(H2_RE.finditer(text))
    headings = [match.group(1).strip() for match in matches]
    sections: dict[str, str] = {}
    for idx, match in enumerate(matches):
        start = match.end()
        end = matches[idx + 1].start() if idx + 1 < len(matches) else len(text)
        sections.setdefault(match.group(1).strip(), text[start:end].strip("\n"))
    return headings, sections


def foundation_document(path: Path) -> FoundationDocument:
    text = path.read_text(encoding="utf-8")
    headings, sections = split_h2_sections(text)
    return FoundationDocument(path=path, text=text, sections=sections, h2_headings=headings)


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
        return MarkdownTable(headers=headers, rows=rows)
    return None


def record_for_row(table: MarkdownTable, row: list[str]) -> dict[str, str]:
    return {header: row[idx] if idx < len(row) else "" for idx, header in enumerate(table.headers)}


def acceptance_matrix_records(doc: FoundationDocument) -> list[dict[str, str]]:
    table = first_markdown_table(doc.section("Acceptance Test Matrix"))
    if table is None or table.headers != ACCEPTANCE_MATRIX_COLUMNS:
        return []
    return [record_for_row(table, row) for row in table.rows]


def implementation_status_table(doc: FoundationDocument) -> MarkdownTable | None:
    callout = implementation_status_callout(doc.text)
    if not callout:
        return None
    return first_markdown_table("\n".join(line.removeprefix(">").strip() for line in callout.splitlines()))


def claims_complete(doc: FoundationDocument) -> bool:
    table = implementation_status_table(doc)
    if table is None or table.headers != IMPLEMENTATION_STATUS_COLUMNS:
        return False
    statuses = [record_for_row(table, row).get("Status", "") for row in table.rows]
    return (
        bool(statuses)
        and any(status == "Done" for status in statuses)
        and all(status in {"Done", "N/A"} for status in statuses)
    )


def table_shape_issues(table: MarkdownTable | None, *, rel: Path, label: str, columns: list[str]) -> list[str]:
    if table is None:
        return [f"{rel}: {label} must contain a markdown table"]
    issues: list[str] = []
    if table.headers != columns:
        issues.append(f"{rel}: {label} table columns must be exactly `{' | '.join(columns)}`")
    for idx, row in enumerate(table.rows, start=1):
        if len(row) != len(table.headers):
            issues.append(f"{rel}: {label} table row {idx} has {len(row)} cells but header has {len(table.headers)}")
    if not table.rows:
        issues.append(f"{rel}: {label} table must have at least one row")
    return issues


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


def check_foundation_inventory_layout() -> list[str]:
    issues: list[str] = []
    if not FOUNDATIONS.is_dir():
        return [f"{FOUNDATIONS.relative_to(ROOT)}: missing foundation inventory directory"]

    for surface_dir in sorted(FOUNDATIONS.iterdir()):
        if surface_dir.name.startswith("_"):
            continue
        rel = surface_dir.relative_to(ROOT)
        if surface_dir.is_file():
            if surface_dir.name != "README.md":
                issues.append(f"{rel}: foundation surfaces must be directories with a README.md hub")
            continue
        if not surface_dir.is_dir():
            continue
        if not (surface_dir / "README.md").is_file():
            issues.append(f"{rel}: missing surface README.md hub")
        for child in sorted(surface_dir.iterdir()):
            child_rel = child.relative_to(ROOT)
            if child.is_dir():
                issues.append(f"{child_rel}: foundations must be direct Markdown files at docs/foundations/{{surface}}/{{slug}}.md")
                continue
            if child.name == "README.md":
                continue
            if child.suffix != ".md":
                issues.append(
                    f"{child_rel}: surface inventories may only contain README.md, foundation .md files, and matching .evidence.md sidecars"
                )
                continue
            if child.name.endswith(".evidence.md"):
                owner = child.with_name(child.name.removesuffix(".evidence.md") + ".md")
                if not owner.is_file():
                    issues.append(f"{child_rel}: evidence sidecar must have a matching foundation file")
    return issues


def iter_foundation_files() -> list[Path]:
    files: list[Path] = []
    for surface_dir in sorted(FOUNDATIONS.iterdir()):
        if not surface_dir.is_dir() or surface_dir.name.startswith("_"):
            continue
        for foundation in sorted(surface_dir.glob("*.md")):
            if foundation.name != "README.md" and not foundation.name.endswith(".evidence.md"):
                files.append(foundation)
    return files


def section_position(doc: FoundationDocument, heading: str) -> int | None:
    if heading == "Implementation status":
        idx = doc.text.find(IMPLEMENTATION_STATUS_HEADING)
        return idx if idx >= 0 else None
    match = re.search(rf"^##\s+{re.escape(heading)}\s*$", doc.text, re.MULTILINE)
    return match.start() if match else None


def validate_sections(doc: FoundationDocument) -> list[str]:
    issues: list[str] = []
    duplicates = sorted({heading for heading in doc.h2_headings if doc.h2_headings.count(heading) > 1})
    for heading in duplicates:
        issues.append(f"{doc.rel}: duplicate `## {heading}` section")
    for heading in REQUIRED_SECTIONS:
        if heading not in doc.sections:
            issues.append(f"{doc.rel}: missing {heading.lower()} section")
    positions = [(heading, pos) for heading in TAIL_SECTION_ORDER if (pos := section_position(doc, heading)) is not None]
    for (previous_heading, previous_position), (current_heading, current_position) in zip(positions, positions[1:], strict=False):
        if previous_position > current_position:
            issues.append(f"{doc.rel}: section order must place `{current_heading}` after `{previous_heading}`")
            break
    return issues


def validate_acceptance_contract(doc: FoundationDocument) -> list[str]:
    issues: list[str] = []
    ac_ids: list[str] = []
    for match in AC_BULLET_RE.finditer(doc.section("Acceptance Criteria")):
        body = match.group("body").strip()
        found = AC_ID_RE.search(body)
        if found is None:
            issues.append(f"{doc.rel}: Acceptance Criteria bullet lacks an AC ID: {body}")
            continue
        ac_ids.append(found.group(0))
        if not AC_BOLD_ID_PREFIX_RE.match(body):
            issues.append(f"{doc.rel}: Acceptance Criteria bullet must use `- **AC-001** ...` format: {body}")
    ac_set = set(ac_ids)
    for ac_id in sorted({ac_id for ac_id in ac_ids if ac_ids.count(ac_id) > 1}):
        issues.append(f"{doc.rel}: duplicate acceptance criterion ID `{ac_id}`")

    table = first_markdown_table(doc.section("Acceptance Test Matrix"))
    issues.extend(table_shape_issues(table, rel=doc.rel, label="Acceptance Test Matrix", columns=ACCEPTANCE_MATRIX_COLUMNS))
    if table is None:
        return issues

    seen_at_ids: set[str] = set()
    required_coverage: set[str] = set()
    for idx, row in enumerate(table.rows, start=1):
        record = record_for_row(table, row)
        row_label = record.get("ID") or f"row {idx}"
        at_id = record.get("ID", "")
        if not AT_ID_RE.fullmatch(at_id):
            issues.append(f"{doc.rel}: Acceptance Test Matrix row {idx} has invalid ID `{at_id}`")
        elif at_id in seen_at_ids:
            issues.append(f"{doc.rel}: duplicate Acceptance Test Matrix ID `{at_id}`")
        else:
            seen_at_ids.add(at_id)

        boundary = record.get("Boundary", "")
        if boundary not in ACCEPTANCE_MATRIX_BOUNDARIES:
            issues.append(f"{doc.rel}: Acceptance Test Matrix {row_label} has invalid Boundary `{boundary}`")
        verification = record.get("Verification", "")
        verification_parts = [part.strip() for part in verification.split("+") if part.strip()]
        if not verification_parts or any(part not in VERIFICATION_VALUES for part in verification_parts):
            issues.append(f"{doc.rel}: Acceptance Test Matrix {row_label} has invalid Verification `{verification}`")
        required = record.get("Required", "")
        if required not in {"Yes", "No"}:
            issues.append(f"{doc.rel}: Acceptance Test Matrix {row_label} Required must be `Yes` or `No`")
        covers = set(AC_ID_RE.findall(record.get("Covers AC", "")))
        if not covers:
            issues.append(f"{doc.rel}: Acceptance Test Matrix {row_label} must cover at least one AC ID")
        unknown = sorted(covers.difference(ac_set))
        if unknown:
            issues.append(f"{doc.rel}: Acceptance Test Matrix {row_label} references unknown AC IDs: {', '.join(unknown)}")
        if required == "Yes":
            required_coverage.update(covers)

    missing_required_coverage = sorted(ac_set.difference(required_coverage))
    if missing_required_coverage:
        issues.append(f"{doc.rel}: Acceptance Test Matrix required rows do not cover AC IDs: {', '.join(missing_required_coverage)}")
    return issues


def verification_evidence_issues(
    ctx: EvidenceValidationContext,
    at_id: str,
    verification: str,
    paths: list[str],
    commands: list[str],
) -> list[str]:
    issues: list[str] = []
    has_e2e_command = any(" local-dev e2e" in command for command in commands)
    has_frontend_test_command = any(
        (" frontend script test" in command and ":e2e" not in command) or command.endswith(" frontend test")
        for command in commands
    )

    if verification == "Browser automation":
        if not any(path.startswith("frontend/e2e/") and path.endswith(".pw.ts") for path in paths):
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Browser automation must reference a committed `frontend/e2e/*.pw.ts` test",
            )
        if not has_e2e_command:
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Browser automation Commands must run Playwright through scripts/axis.py",
            )
    elif verification == "UI component test":
        if not any(
            path.startswith(("frontend/src/", "frontend/tests/")) and path.endswith((".test.ts", ".test.tsx"))
            for path in paths
        ):
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} UI component test must reference a committed frontend `*.test.ts` or `*.test.tsx` file",
            )
        if not has_frontend_test_command:
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} UI component test Commands must run frontend tests through scripts/axis.py",
            )
    elif verification == "Frontend CI":
        if not any("python scripts/axis.py frontend ci" in command for command in commands):
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Frontend CI Commands must include `python scripts/axis.py frontend ci`",
            )
    elif verification == "Browser-capable visual smoke":
        if not any(path.startswith(("frontend/e2e/", "docs/foundations/")) for path in paths):
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Browser-capable visual smoke must reference committed browser test or foundation artifact",
            )
    return issues


def validate_acceptance_evidence(doc: FoundationDocument) -> list[str]:
    ctx = EvidenceValidationContext(
        root=ROOT,
        owner_rel=doc.rel,
        owner_sections=doc.sections,
        evidence_rel=evidence_file_for(doc.path).relative_to(ROOT),
        matrix_records=acceptance_matrix_records(doc),
        complete=claims_complete(doc),
        label="foundation",
    )
    return validate_acceptance_evidence_sidecar(ctx, verification_evidence_issues)


def validate_implementation_status(doc: FoundationDocument) -> list[str]:
    issues: list[str] = []
    callout = implementation_status_callout(doc.text)
    if not callout:
        return [f"{doc.rel}: missing implementation status callout"]
    status_table = implementation_status_table(doc)
    issues.extend(table_shape_issues(status_table, rel=doc.rel, label="Implementation status", columns=IMPLEMENTATION_STATUS_COLUMNS))
    if status_table is not None:
        for idx, row in enumerate(status_table.rows, start=1):
            status = record_for_row(status_table, row).get("Status", "")
            if status not in IMPLEMENTATION_STATUS_VALUES:
                issues.append(f"{doc.rel}: Implementation status row {idx} has invalid Status `{status}`")
    for marker, label in IMPLEMENTATION_STATUS_REQUIRED_MARKERS:
        if marker not in callout:
            issues.append(f"{doc.rel}: missing implementation status {label} section")
    return issues


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate and exit non-zero on issues")
    _ = parser.parse_args()

    issues: list[str] = []
    issues.extend(check_foundation_inventory_layout())
    for path in iter_foundation_files():
        doc = foundation_document(path)
        issues.extend(validate_sections(doc))
        issues.extend(validate_acceptance_contract(doc))
        issues.extend(validate_acceptance_evidence(doc))
        issues.extend(validate_implementation_status(doc))

    if issues:
        print("Foundation docs validation failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print("check-foundation-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
