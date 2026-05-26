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

LAYER_PART = re.compile(
    r"^(.+?): (.+)$"
)

DEPRECATED_STATUS = re.compile(r"^\> \*\*Implementation status\*\* — ")
DEPRECATED_WIREFRAME = re.compile(r"^\> \*\*Wireframe\*\*:")


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


def format_status_block(lines: list[str]) -> list[str]:
    if not lines:
        return lines

    header_match = STATUS_HEADER.match(lines[0])
    if not header_match:
        return lines

    rows = sort_layers(parse_layers(header_match.group(1)))
    body_lines = [line[2:].strip() for line in lines[1:] if line.startswith("> ")]

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
        if body.startswith("Gaps vs spec:"):
            out.append(f"> **Gaps vs spec:** {body[len('Gaps vs spec:'):].strip()}")
        elif body.startswith("Decisions:"):
            out.append(f"> **Decisions:** {body[len('Decisions:'):].strip()}")
        elif body.startswith("**"):
            out.append(f"> {body}")
        else:
            out.append(f"> {body}")

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

    out = [
        "## Wireframes",
        "",
        "| Screen | Excalidraw | Preview |",
        "|--------|------------|---------|",
    ]
    for slug, excalidraw, preview in entries:
        out.append(
            f"| {slug} | [source]({excalidraw}) | [preview]({preview}) |"
        )
    out.append("")
    return out


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
    changed = False
    output: list[str] = []
    index = 0

    while index < len(lines):
        line = lines[index]

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
                changed = True
            continue

        if STATUS_HEADER.match(line):
            status_run = [line]
            index += 1
            while index < len(lines) and lines[index].startswith("> ") and not STATUS_HEADER.match(lines[index]):
                status_run.append(lines[index])
                index += 1
            formatted = format_status_block(status_run)
            if formatted != status_run:
                changed = True
            output.extend(formatted)
            continue

        output.append(line)
        index += 1

    new_text = normalize_content("\n".join(output) + ("\n" if original.endswith("\n") else ""))
    if new_text != original:
        changed = True
    return new_text, changed


def check_deprecated_format(path: Path) -> list[str]:
    issues: list[str] = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if DEPRECATED_STATUS.match(line):
            issues.append(f"{path}:{line_number}: deprecated single-line Implementation status")
        if DEPRECATED_WIREFRAME.match(line):
            issues.append(f"{path}:{line_number}: deprecated inline Wireframe blockquote")
    return issues


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

    for path in sorted(ROOT.glob(FEATURE_GLOB)):
        deprecated_issues.extend(check_deprecated_format(path))
        if process_file(path, check_only=args.check):
            changed_files.append(str(path.relative_to(ROOT)))

    exit_code = 0

    if deprecated_issues:
        exit_code = 1
        print("Deprecated feature file format detected:", file=sys.stderr)
        for issue in deprecated_issues:
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
