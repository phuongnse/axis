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

    if "| Screen | Excalidraw | Preview |" not in text:
        issues.append(f"{rel}: missing wireframes table header")

    if "| Diagram | Source | Preview |" not in text:
        issues.append(f"{rel}: missing diagrams table header")

    if "> **Implementation status**" not in text:
        issues.append(f"{rel}: missing implementation status callout")

    return issues


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate and exit non-zero on issues")
    _ = parser.parse_args()

    issues: list[str] = []
    for path in iter_use_case_files():
        issues.extend(check_file(path))

    if issues:
        print("Use-case docs validation failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print("check-use-case-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
