#!/usr/bin/env python3
"""Validate flow-first use-case docs structure.

Usage:
  python3 scripts/check-use-case-docs.py --check
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = set()

REQUIRED_HEADINGS = ["## Wireframes", "## Diagrams"]

# Placeholder markers from USE_CASE_TEMPLATE.md that must be replaced before a
# use case can be considered written. Listed individually so the validator can
# point at the missing field.
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


def check_file(path: Path) -> list[str]:
    issues: list[str] = []
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT)

    if re.search(r"^#\s*F\d+\s*[—-]\s*", text, re.MULTILINE):
        issues.append(f"{rel}: legacy Fxx title found")

    for heading in REQUIRED_HEADINGS:
        if heading not in text:
            issues.append(f"{rel}: missing required heading `{heading}`")

    has_purpose = "## Purpose" in text
    has_actor = "## Primary actor" in text
    has_trigger = "## Trigger" in text
    has_main_flow = "## Main flow" in text
    has_alt_flow = "## Alternate / error flows" in text
    has_ac = "## Acceptance Criteria" in text

    if not has_purpose:
        issues.append(f"{rel}: missing purpose section")
    if not has_actor:
        issues.append(f"{rel}: missing actor section")
    if not has_trigger:
        issues.append(f"{rel}: missing trigger section")
    if not has_main_flow:
        issues.append(f"{rel}: missing main flow section")
    if not has_alt_flow:
        issues.append(f"{rel}: missing alternate/error flows section")
    if not has_ac:
        issues.append(f"{rel}: missing acceptance criteria section")

    wireframes_section = ""
    if "## Wireframes" in text:
        wireframes_section = text.split("## Wireframes", 1)[1].split("\n## ", 1)[0]
    if not (
        "| Screen |" in wireframes_section
        and "| Excalidraw |" in wireframes_section
        and "| Preview |" in wireframes_section
    ):
        issues.append(f"{rel}: missing wireframes table (need Screen, Excalidraw, Preview columns)")

    if "| Diagram | Source | Preview |" not in text:
        issues.append(f"{rel}: missing diagrams table header")

    if "> **Implementation status**" not in text:
        issues.append(f"{rel}: missing implementation status callout")

    for placeholder in PURPOSE_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Purpose still has placeholder `{placeholder}`")
    for placeholder in ACTOR_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Primary actor still has placeholder `{placeholder.strip()}`")
    for placeholder in TRIGGER_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Trigger still has placeholder `{placeholder.strip()}`")

    return issues


def count_template_main_flow(files: list[Path]) -> int:
    return sum(1 for p in files if TEMPLATE_MAIN_FLOW in p.read_text(encoding="utf-8"))


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
    for path in files:
        issues.extend(check_file(path))
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
