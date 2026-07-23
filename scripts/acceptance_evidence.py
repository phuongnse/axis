from __future__ import annotations

import re
import shlex
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

ACCEPTANCE_EVIDENCE_COLUMNS = ["AT ID", "Evidence", "Commands"]
AT_ID_RE = re.compile(r"^AT-\d{3}$")
H2_RE = re.compile(r"^##\s+(.+?)\s*$", re.MULTILINE)
INLINE_CODE_RE = re.compile(r"`([^`]+)`")


def axis_command_args(command: str) -> list[str] | None:
    try:
        tokens = shlex.split(command)
    except ValueError:
        return None
    if tokens[:2] != ["python", "scripts/axis.py"]:
        return None
    return tokens[2:]


def is_browser_e2e_command(command: str) -> bool:
    args = axis_command_args(command)
    return args is not None and args[:2] == ["local-dev", "e2e"]


def is_frontend_component_test_command(command: str) -> bool:
    args = axis_command_args(command)
    if args is None:
        return False
    if args[:2] == ["frontend", "test"]:
        return True
    if args[:2] != ["frontend", "script"] or len(args) < 3:
        return False
    script_parts = args[2].split(":")
    return script_parts[0] == "test" and "e2e" not in script_parts[1:]


@dataclass(frozen=True)
class MarkdownTable:
    headers: list[str]
    rows: list[list[str]]


@dataclass(frozen=True)
class EvidenceValidationContext:
    root: Path
    owner_rel: Path
    owner_sections: dict[str, str]
    evidence_rel: Path
    matrix_records: list[dict[str, str]]
    complete: bool
    label: str


VerificationValidator = Callable[
    [EvidenceValidationContext, str, str, list[str], list[str]],
    list[str],
]


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


def split_h2_sections(text: str) -> dict[str, str]:
    matches = list(H2_RE.finditer(text))
    sections: dict[str, str] = {}
    for idx, match in enumerate(matches):
        start = match.end()
        end = matches[idx + 1].start() if idx + 1 < len(matches) else len(text)
        sections.setdefault(match.group(1).strip(), text[start:end].strip("\n"))
    return sections


def inline_code_values(text: str) -> list[str]:
    return [match.group(1).strip() for match in INLINE_CODE_RE.finditer(text)]


def evidence_file_for(owner_path: Path) -> Path:
    return owner_path.with_name(f"{owner_path.stem}.evidence.md")


def at_ids_for_cell(value: str) -> list[str]:
    return [part.strip() for part in value.split(",") if part.strip()]


def table_shape_issues(table: MarkdownTable | None, *, rel: Path, label: str) -> list[str]:
    if table is None:
        return [f"{rel}: {label} must contain a markdown table"]
    issues: list[str] = []
    if table.headers != ACCEPTANCE_EVIDENCE_COLUMNS:
        issues.append(
            f"{rel}: {label} table columns must be exactly `{' | '.join(ACCEPTANCE_EVIDENCE_COLUMNS)}`"
        )
    for idx, row in enumerate(table.rows, start=1):
        if len(row) != len(table.headers):
            issues.append(
                f"{rel}: {label} table row {idx} has {len(row)} cells but header has {len(table.headers)}"
            )
    if not table.rows:
        issues.append(f"{rel}: {label} table must have at least one row")
    return issues


def evidence_path_issues(ctx: EvidenceValidationContext, at_id: str, evidence: str) -> tuple[list[str], list[str]]:
    issues: list[str] = []
    paths = inline_code_values(evidence)
    if not paths:
        return [f"{ctx.evidence_rel}: Acceptance Evidence {at_id} must list at least one backticked repo path"], []

    for path_text in paths:
        if path_text.startswith(("python ", "npm ", "npx ", "dotnet ", "docker ")):
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Evidence must list files, not commands: `{path_text}`"
            )
            continue
        path = Path(path_text)
        if path.is_absolute() or ".." in path.parts:
            issues.append(
                f"{ctx.evidence_rel}: Acceptance Evidence {at_id} path must be a repo-relative path: `{path_text}`"
            )
            continue
        if not (ctx.root / path).is_file():
            issues.append(f"{ctx.evidence_rel}: Acceptance Evidence {at_id} path does not exist: `{path_text}`")
    return issues, paths


def command_issues(ctx: EvidenceValidationContext, at_id: str, commands_text: str) -> tuple[list[str], list[str]]:
    commands = inline_code_values(commands_text)
    if not commands:
        return [f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Commands must list backticked Axis commands"], []

    issues: list[str] = []
    for command in commands:
        if not command.startswith("python scripts/axis.py "):
            issues.append(f"{ctx.evidence_rel}: Acceptance Evidence {at_id} Commands must use the Axis wrapper")
    return issues, commands


def validate_acceptance_evidence_sidecar(
    ctx: EvidenceValidationContext,
    verification_validator: VerificationValidator,
) -> list[str]:
    issues: list[str] = []
    required_records = {
        record["ID"]: record
        for record in ctx.matrix_records
        if AT_ID_RE.fullmatch(record.get("ID", "")) and record.get("Required") == "Yes"
    }
    known_at_ids = {record.get("ID", "") for record in ctx.matrix_records}

    if "Acceptance Evidence" in ctx.owner_sections:
        issues.append(
            f"{ctx.owner_rel}: Acceptance Evidence belongs in sidecar `{ctx.evidence_rel}`, not the spec file"
        )

    evidence_path = ctx.root / ctx.evidence_rel
    if not evidence_path.is_file():
        if ctx.complete and required_records:
            issues.append(
                f"{ctx.owner_rel}: complete {ctx.label} docs must include acceptance evidence sidecar `{ctx.evidence_rel}`"
            )
        return issues

    sections = split_h2_sections(evidence_path.read_text(encoding="utf-8"))
    table = first_markdown_table(sections.get("Acceptance Evidence", ""))
    issues.extend(table_shape_issues(table, rel=ctx.evidence_rel, label="Acceptance Evidence"))
    if table is None or table.headers != ACCEPTANCE_EVIDENCE_COLUMNS:
        return issues

    seen: set[str] = set()
    for idx, row in enumerate(table.rows, start=1):
        record = record_for_row(table, row)
        at_id_cell = record.get("AT ID", "")
        at_ids = at_ids_for_cell(at_id_cell)
        invalid_at_ids = [at_id for at_id in at_ids if not AT_ID_RE.fullmatch(at_id)]
        if not at_ids or invalid_at_ids:
            issues.append(f"{ctx.evidence_rel}: Acceptance Evidence row {idx} has invalid AT ID list `{at_id_cell}`")
            continue

        path_issues, paths = evidence_path_issues(ctx, at_id_cell, record.get("Evidence", ""))
        issues.extend(path_issues)
        command_errors, commands = command_issues(ctx, at_id_cell, record.get("Commands", ""))
        issues.extend(command_errors)
        for at_id in at_ids:
            if at_id in seen:
                issues.append(f"{ctx.evidence_rel}: duplicate Acceptance Evidence AT ID `{at_id}`")
                continue
            seen.add(at_id)
            if at_id not in known_at_ids:
                issues.append(f"{ctx.evidence_rel}: Acceptance Evidence {at_id} references unknown Acceptance Test Matrix ID")
                continue

            matrix_record = required_records.get(at_id)
            if matrix_record is not None:
                verification_parts = [
                    part.strip() for part in matrix_record.get("Verification", "").split("+") if part.strip()
                ]
                for verification in verification_parts:
                    issues.extend(verification_validator(ctx, at_id, verification, paths, commands))

    missing_required = sorted(set(required_records).difference(seen))
    if ctx.complete and missing_required:
        issues.append(f"{ctx.evidence_rel}: Acceptance Evidence missing required AT IDs: {', '.join(missing_required)}")
    return issues
