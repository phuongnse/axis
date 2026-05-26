#!/usr/bin/env python3
"""Normalize feature file wireframe lists and Implementation status callouts.

Usage:
  python3 scripts/normalize-feature-docs.py          # rewrite files in place
  python3 scripts/normalize-feature-docs.py --check  # exit 1 if rewrite needed (CI)
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FEATURE_GLOB = "docs/epics/**/features/*.md"

LAYER_ORDER = [
    "Domain",
    "Application",
    "Infrastructure",
    "Contracts",
    "API",
    "Frontend",
]

WIREFRAME_LINE = re.compile(
    r"^> \*\*Wireframe\*\*: \[(?:[^\]]+)\]\(([^)]+)\) · \[preview\]\(([^)]+)\)\s*$"
)

STATUS_HEADER = re.compile(
    r"^> \*\*Implementation status\*\* — (.+)\s*$"
)

MODERN_STATUS_START = re.compile(r"^> \*\*Implementation status\*\*\s*$")

WIREFRAME_SECTION = re.compile(r"^## Wireframes\s*$")

WIREFRAME_TABLE_ROW = re.compile(
    r"^\| ([^|]+) \| \[source\]\(([^)]+)\) \| \[preview\]\(([^)]+)\) \|"
)

STATUS_FIELD_SPLIT = re.compile(
    r"\*\*(Gaps vs spec|Done|Deferred(?:\s*\(PR\s*#[^)]+\s*follow-up\))?|Decisions):\*\*\s*"
)

LAYER_PART = re.compile(
    r"^(.+?): (.+)$"
)

DEPRECATED_STATUS = re.compile(r"^\> \*\*Implementation status\*\* — ")
DEPRECATED_WIREFRAME = re.compile(r"^\> \*\*Wireframe\*\*:")


def is_blockquote_line(line: str) -> bool:
    """True for markdown blockquote lines, including empty `>`."""
    stripped = line.lstrip()
    return stripped.startswith(">")


def slug_from_excalidraw(path: str) -> str:
    name = Path(path).name
    if name.endswith(".excalidraw"):
        return name[: -len(".excalidraw")]
    return name


def parse_layers(header_rest: str) -> list[tuple[str, str]]:
    rows: list[tuple[str, str]] = []
    for part in header_rest.split(" | "):
        part = part.strip()
        match = LAYER_PART.match(part)
        if not match:
            continue
        layer_label, status = match.group(1).strip(), match.group(2).strip()
        if "+" in layer_label:
            for layer in (item.strip() for item in layer_label.split("+")):
                rows.append((layer, status))
        else:
            rows.append((layer_label, status))
    return rows


def sort_layers(rows: list[tuple[str, str]]) -> list[tuple[str, str]]:
    order_index = {name: index for index, name in enumerate(LAYER_ORDER)}
    return sorted(rows, key=lambda item: order_index.get(item[0], len(LAYER_ORDER)))


def split_status_fields(text: str) -> list[tuple[str, str]]:
    """Split a single line that may contain **Gaps** … **Done** … into labeled sections."""
    text = text.strip()
    if not text:
        return []

    if not STATUS_FIELD_SPLIT.search(text):
        if text.startswith("Gaps vs spec:"):
            return [("Gaps vs spec", text[len("Gaps vs spec:") :].strip())]
        if text.startswith("**"):
            match = re.match(r"^\*\*([^*]+):\*\*\s*(.*)$", text, re.DOTALL)
            if match:
                return [(match.group(1).strip(), match.group(2).strip())]
        return [("", text)]

    parts = STATUS_FIELD_SPLIT.split(text)
    fields: list[tuple[str, str]] = []
    index = 1
    while index < len(parts):
        label = parts[index].strip()
        content = parts[index + 1].strip() if index + 1 < len(parts) else ""
        if label:
            fields.append((label, content))
        index += 2
    return fields


def format_status_body_paragraphs(body_text: str) -> list[str]:
    """Emit blockquote lines for Gaps / Done / Deferred / Decisions with spacing."""
    out: list[str] = []
    fields = split_status_fields(body_text)

    for index, (label, content) in enumerate(fields):
        if index > 0:
            out.append(">")

        if not label:
            if content:
                out.append(f"> {content}")
            continue

        if label == "Gaps vs spec" and content.lower().startswith("none"):
            out.append(f"> **Gaps vs spec:** {content}")
            continue

        list_items = bullet_lines_from_content(content)
        if list_items is not None:
            if len(list_items) == 1:
                out.append(f"> **{label}:** {list_items[0]}")
            else:
                out.append(f"> **{label}:**")
                for item in list_items:
                    out.append(f"> - {item}")
            continue

        bullet_items = split_bullet_candidates(content)
        if len(bullet_items) > 1:
            out.append(f"> **{label}:**")
            for item in bullet_items:
                out.append(f"> - {item}")
        else:
            out.append(f"> **{label}:** {content}")

    return out


def coalesce_body_parts(parts: list[str]) -> list[str]:
    """Attach blockquote bullet lines to the preceding status field header."""
    merged: list[str] = []
    for part in parts:
        stripped = part.strip()
        if stripped.startswith("- ") and merged:
            merged[-1] = f"{merged[-1]}\n{stripped}"
            continue
        merged.append(part)
    return merged


def bullet_lines_from_content(content: str) -> list[str] | None:
    """If content is only markdown list items, return item texts without leading '- '."""
    lines = [line.strip() for line in content.splitlines() if line.strip()]
    if not lines or not all(line.startswith("- ") for line in lines):
        return None
    return [line[2:].strip() for line in lines]


def split_bullet_candidates(content: str) -> list[str]:
    """Split long semicolon-separated backend notes into list items when helpful."""
    if "; " not in content or len(content) < 120:
        return [content] if content else []

    segments = [segment.strip() for segment in content.split("; ") if segment.strip()]
    if len(segments) < 2:
        return [content]

    return segments


def blockquote_content(line: str) -> str:
    """Strip leading `>` and optional space from a blockquote line."""
    if not is_blockquote_line(line):
        return ""
    after_marker = line.lstrip()[1:]
    if after_marker.startswith(" "):
        after_marker = after_marker[1:]
    return after_marker.strip()


def parse_modern_status_block(lines: list[str]) -> tuple[list[tuple[str, str]], list[str]]:
    """Return (layer_rows, raw_body_strings) from a modern status blockquote."""
    layer_rows: list[tuple[str, str]] = []
    body_parts: list[str] = []
    seen_layer_header = False

    for line in lines[1:]:
        if not is_blockquote_line(line):
            continue
        content = blockquote_content(line)
        if not content:
            continue
        if content.startswith("|"):
            if content.startswith("|-"):
                continue
            cells = [cell.strip() for cell in content.strip("|").split("|")]
            if len(cells) < 2 or cells[0].lower() == "layer":
                seen_layer_header = True
                continue
            layer_rows.append((cells[0], cells[1]))
            continue
        body_parts.append(content)

    return layer_rows, coalesce_body_parts(body_parts)


def format_modern_status_block(lines: list[str]) -> list[str]:
    if not lines or not MODERN_STATUS_START.match(lines[0]):
        return lines

    layer_rows, body_parts = parse_modern_status_block(lines)
    if not layer_rows and len(lines) > 1 and STATUS_HEADER.match(lines[0]):
        return format_status_block(lines)

    if layer_rows:
        rows = sort_layers(layer_rows)
    else:
        header_match = STATUS_HEADER.match(lines[0])
        rows = sort_layers(parse_layers(header_match.group(1))) if header_match else []

    out: list[str] = [
        "> **Implementation status**",
        ">",
        "> | Layer | Status |",
        "> |-------|--------|",
    ]
    for layer, status in rows:
        out.append(f"> | {layer} | {status} |")
    out.append(">")

    for body_index, body in enumerate(body_parts):
        if body_index > 0:
            out.append(">")
        out.extend(format_status_body_paragraphs(body))

    return out


def format_status_block(lines: list[str]) -> list[str]:
    if not lines:
        return lines

    if MODERN_STATUS_START.match(lines[0]):
        return format_modern_status_block(lines)

    header_match = STATUS_HEADER.match(lines[0])
    if not header_match:
        return lines

    rows = sort_layers(parse_layers(header_match.group(1)))
    body_lines = [
        blockquote_content(line)
        for line in lines[1:]
        if is_blockquote_line(line) and blockquote_content(line) and not blockquote_content(line).startswith("|")
    ]

    out: list[str] = [
        "> **Implementation status**",
        ">",
        "> | Layer | Status |",
        "> |-------|--------|",
    ]
    for layer, status in rows:
        out.append(f"> | {layer} | {status} |")
    out.append(">")

    for body in body_lines:
        out.extend(format_status_body_paragraphs(body))

    return out


def format_wireframes_from_table_rows(
    entries: list[tuple[str, str, str]],
) -> list[str]:
    out = [
        "## Wireframes",
        "",
        "| Screen | Excalidraw | Preview |",
        "|--------|------------|---------|",
    ]
    for screen, excalidraw, preview in entries:
        out.append(f"| {screen} | [source]({excalidraw}) | [preview]({preview}) |")
    out.append("")
    return out


def format_wireframes(lines: list[str]) -> list[str] | None:
    entries: list[tuple[str, str, str]] = []
    for line in lines:
        match = WIREFRAME_LINE.match(line)
        if not match:
            return None
        excalidraw, preview = match.group(1), match.group(2)
        entries.append((slug_from_excalidraw(excalidraw), excalidraw, preview))

    if not entries:
        return None

    return format_wireframes_from_table_rows(entries)


def merge_wireframe_tables(lines: list[str], start: int) -> tuple[list[str] | None, int]:
    """Merge consecutive wireframe markdown tables into one section."""
    if start >= len(lines) or not WIREFRAME_SECTION.match(lines[start]):
        return None, start

    entries: list[tuple[str, str, str]] = []
    index = start + 1

    while index < len(lines):
        line = lines[index]
        if line.strip() == "":
            index += 1
            continue
        if line.startswith("| Screen |"):
            index += 1
            if index < len(lines) and lines[index].startswith("|-"):
                index += 1
            while index < len(lines):
                row_match = WIREFRAME_TABLE_ROW.match(lines[index])
                if not row_match:
                    break
                entries.append(
                    (
                        row_match.group(1).strip(),
                        row_match.group(2).strip(),
                        row_match.group(3).strip(),
                    )
                )
                index += 1
            continue
        break

    if not entries:
        return None, start

    return format_wireframes_from_table_rows(entries), index


def normalize_content(text: str) -> str:
    replacements = [
        (
            "pending API layer",
            "backend polish — see gaps below",
        ),
        (
            "pending API query layer",
            "query projection polish — see gaps below",
        ),
        (
            "pending auth infrastructure (OpenIddict + Redis)",
            "auth infrastructure polish — see gaps below",
        ),
        (
            "pending API/Infrastructure concern",
            "API/Infrastructure polish",
        ),
    ]
    for old, new in replacements:
        text = text.replace(old, new)
    return text


def transform_text(original: str) -> tuple[str, bool]:
    lines = original.splitlines()
    output: list[str] = []
    index = 0

    while index < len(lines):
        line = lines[index]

        merged, next_index = merge_wireframe_tables(lines, index)
        if merged is not None:
            output.extend(merged)
            index = next_index
            continue

        if WIREFRAME_LINE.match(line):
            wireframe_run = [line]
            index += 1
            while index < len(lines) and WIREFRAME_LINE.match(lines[index]):
                wireframe_run.append(lines[index])
                index += 1
            formatted = format_wireframes(wireframe_run)
            if formatted is None:
                output.extend(wireframe_run)
            else:
                output.extend(formatted)
            continue

        if MODERN_STATUS_START.match(line) or STATUS_HEADER.match(line):
            status_run = [line]
            index += 1
            while index < len(lines):
                next_line = lines[index]
                if MODERN_STATUS_START.match(next_line):
                    break
                if is_blockquote_line(next_line):
                    status_run.append(next_line)
                    index += 1
                    continue
                if next_line.strip() == "" and index + 1 < len(lines) and is_blockquote_line(
                    lines[index + 1]
                ):
                    index += 1
                    continue
                break
            output.extend(format_status_block(status_run))
            continue

        output.append(line)
        index += 1

    new_text = normalize_content("\n".join(output) + ("\n" if original.endswith("\n") else ""))
    return new_text, new_text != original


def check_deprecated_format(path: Path) -> list[str]:
    issues: list[str] = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if DEPRECATED_STATUS.match(line):
            issues.append(f"{path}:{line_number}: deprecated single-line Implementation status")
        if DEPRECATED_WIREFRAME.match(line):
            issues.append(f"{path}:{line_number}: deprecated inline Wireframe blockquote")
    return issues


def check_duplicate_wireframe_tables(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    count = len(re.findall(r"^\| Screen \| Excalidraw \| Preview \|", text, re.MULTILINE))
    if count > 1:
        return [f"{path}: {count} wireframe tables — merge into one ## Wireframes table"]
    return []


def process_file(path: Path, check_only: bool) -> bool:
    original = path.read_text(encoding="utf-8")
    new_text, changed = transform_text(original)
    if changed and not check_only:
        path.write_text(new_text, encoding="utf-8")
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Do not write files; exit 1 if any feature file needs normalization",
    )
    args = parser.parse_args()

    changed_files: list[str] = []
    deprecated_issues: list[str] = []
    structure_issues: list[str] = []

    for path in sorted(ROOT.glob(FEATURE_GLOB)):
        if process_file(path, check_only=args.check):
            changed_files.append(str(path.relative_to(ROOT)))
        if args.check:
            deprecated_issues.extend(check_deprecated_format(path))
            structure_issues.extend(check_duplicate_wireframe_tables(path))

    exit_code = 0

    if deprecated_issues:
        exit_code = 1
        print("Deprecated feature file format detected:", file=sys.stderr)
        for issue in deprecated_issues:
            print(f"  {issue}", file=sys.stderr)
        print("Run: python3 scripts/normalize-feature-docs.py", file=sys.stderr)

    if structure_issues:
        exit_code = 1
        print("Feature file structure issues:", file=sys.stderr)
        for issue in structure_issues:
            print(f"  {issue}", file=sys.stderr)
        print("Run: python3 scripts/normalize-feature-docs.py", file=sys.stderr)

    if changed_files:
        exit_code = 1
        mode = "needs normalization" if args.check else "normalized"
        print(f"Feature files {mode} ({len(changed_files)}):", file=sys.stderr)
        for file_path in changed_files:
            print(f"  - {file_path}", file=sys.stderr)
        if args.check:
            print("Run: python3 scripts/normalize-feature-docs.py", file=sys.stderr)
        else:
            print(f"Normalized {len(changed_files)} feature file(s):")
            for file_path in changed_files:
                print(f"  - {file_path}")

    if exit_code == 0:
        print("normalize-feature-docs: OK")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
