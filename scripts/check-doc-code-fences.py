#!/usr/bin/env python3
"""Detect fenced code blocks whose indentation was flattened to a single space.

A bulk find-replace over the docs tree once collapsed every run of two or more
spaces to one (PR #146 post-mortem). Inside fenced code blocks this silently
destroyed all indentation — `    .Where(...)` became ` .Where(...)` — so code
samples in the docs no longer copy-paste correctly. Every existing gate passed:

  - lychee / check-doc-link-targets only validate links and anchors (none broke).
  - check-use-case-docs only validates structure (sections/tables survived).
  - prettier collapses prose whitespace anyway and never touches the inside of a
    code fence for an unsupported language (C#), so it neither flags nor fixes it.
  - dotnet format / Biome only cover `src/` and `frontend/`, never `.md`.

This script closes that blind spot with a precise, zero-false-positive signal:
a line *inside a fenced code block* that starts with exactly one space followed
by a non-space character. Real code indents in multiples of 2/4; a lone leading
space is the fingerprint of collapsed indentation.

`diff` / `patch` fences are exempt — their context lines legitimately begin with
a single space.

Usage:
  python3 scripts/check-doc-code-fences.py --check
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

SCAN_ROOTS = [
    ROOT / "docs",
    ROOT / ".github",
]
SCAN_FILES_AT_ROOT = ["CLAUDE.md", "CONTRIBUTING.md", "README.md"]

# Fenced code block opener: ``` or ~~~ (CommonMark allows up to 3 leading
# spaces), capturing the fence marker and the info string (language).
FENCE_RE = re.compile(r"^(?: {0,3})(`{3,}|~{3,})\s*([^\s`]*)")

# A line inside a fence that starts with exactly one space then a non-space.
COLLAPSED_INDENT_RE = re.compile(r"^ [^ \t]")

# Info strings whose lines legitimately start with a single space.
EXEMPT_LANGS = {"diff", "patch"}


def iter_md_files() -> list[Path]:
    files: list[Path] = []
    for root in SCAN_ROOTS:
        if root.is_dir():
            files.extend(sorted(root.rglob("*.md")))
    for name in SCAN_FILES_AT_ROOT:
        path = ROOT / name
        if path.is_file():
            files.append(path)
    return files


def check_file(path: Path) -> list[str]:
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        return [f"{path.relative_to(ROOT)}: cannot decode as UTF-8"]

    rel = path.relative_to(ROOT)
    issues: list[str] = []
    in_fence = False
    fence_marker: str | None = None
    exempt = False

    for line_no, line in enumerate(text.splitlines(), 1):
        fence_match = FENCE_RE.match(line)
        if fence_match:
            marker = fence_match.group(1)
            if not in_fence:
                in_fence = True
                fence_marker = marker[:3]
                exempt = fence_match.group(2).lower() in EXEMPT_LANGS
            elif fence_marker and line.lstrip().startswith(fence_marker):
                in_fence = False
                fence_marker = None
                exempt = False
            continue
        if in_fence and not exempt and COLLAPSED_INDENT_RE.match(line):
            issues.append(
                f"{rel}:{line_no}: code-fence line indented by a single space "
                f"(collapsed indentation?): {line!r}"
            )

    return issues


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="validate and exit non-zero on issues",
    )
    _ = parser.parse_args()

    files = iter_md_files()
    issues: list[str] = []
    for path in files:
        issues.extend(check_file(path))

    if issues:
        print("Doc code-fence indentation check failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print(f"check-doc-code-fences: OK ({len(files)} files scanned)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
