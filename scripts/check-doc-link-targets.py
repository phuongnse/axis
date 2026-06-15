#!/usr/bin/env python3
"""Validate that every relative markdown link and image target resolves.

CI's lychee job catches dead anchors and external 404s, but it misses
broken `![alt](./relative.svg)` image refs. This script is the explicit,
file-by-file check that closes that gap.

Rules:
- Scan every `.md` under `docs/`, plus repo-root markdown (`AGENTS.md`,
  `CONTRIBUTING.md`, `README.md`) and `.github/`.
- For each `[text](path)` and `![alt](path)`:
  - Skip absolute URLs (`http://`, `https://`, `mailto:`).
  - Skip pure anchors (`#section`).
  - Resolve relative path against the source file's directory.
  - Fail if the target file does not exist.

Usage:
  python3 scripts/check-doc-link-targets.py --check
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Roots scanned for `.md`. Add new roots conservatively — the cost of a false
# positive in CI is wasted developer time, the cost of a missed broken link
# is a doc that ships with a dead image reference.
SCAN_ROOTS = [
    ROOT / "docs",
    ROOT / ".github",
]
SCAN_FILES_AT_ROOT = ["AGENTS.md", "CONTRIBUTING.md", "README.md"]

# Markdown link/image targets. Capture group 1 = target path/URL.
# Matches both `[text](target)` and `![alt](target)`.
LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")

# Inline-code spans `like this` — content inside is illustrative, not a real
# link. Used to strip those before LINK_RE runs.
INLINE_CODE_RE = re.compile(r"`[^`\n]*`")

# Fenced code block opener/closer (``` or ~~~, optionally with a language).
FENCE_RE = re.compile(r"^(?:\s{0,3})(```+|~~~+)")

ABSOLUTE_SCHEMES = ("http://", "https://", "mailto:", "ftp://", "tel:")


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


def is_skipped_target(target: str) -> bool:
    if target.startswith(ABSOLUTE_SCHEMES):
        return True
    if target.startswith("#"):
        return True
    # Angle-bracket auto-links (<https://...>) leak `<` into target; ignore.
    if target.startswith("<") and target.endswith(">"):
        inner = target[1:-1]
        if inner.startswith(ABSOLUTE_SCHEMES):
            return True
    return False


def resolve_target(source: Path, target: str) -> Path:
    # Strip trailing `#anchor` — file existence is what we check; lychee
    # validates the anchor itself.
    path_part = target.split("#", 1)[0]
    if not path_part:
        # Pure anchor (e.g. `#section`) — caller has already skipped these,
        # but guard anyway.
        return source
    candidate = (source.parent / path_part).resolve()
    return candidate


def _strip_code(text: str) -> str:
    """Blank out fenced code blocks and inline backtick spans.

    Replacement preserves line breaks (so error line numbers stay accurate)
    but removes the content lychee/this script would otherwise try to
    resolve. Without this, illustrative examples like
    ``[text](./file.md)`` inside backticks would false-positive.
    """
    out_lines: list[str] = []
    in_fence = False
    fence_marker: str | None = None
    for line in text.splitlines():
        fence_match = FENCE_RE.match(line)
        if fence_match:
            marker = fence_match.group(1)
            if not in_fence:
                in_fence = True
                fence_marker = marker[:3]  # ``` or ~~~
            elif fence_marker and line.lstrip().startswith(fence_marker):
                in_fence = False
                fence_marker = None
            out_lines.append("")  # drop fence content + the fence line itself
            continue
        if in_fence:
            out_lines.append("")
            continue
        # Drop inline backtick spans on this line.
        out_lines.append(INLINE_CODE_RE.sub("", line))
    return "\n".join(out_lines)


def check_file(path: Path) -> list[str]:
    issues: list[str] = []
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        return [f"{path.relative_to(ROOT)}: cannot decode as UTF-8"]

    rel = path.relative_to(ROOT)
    scanned = _strip_code(text)

    for match in LINK_RE.finditer(scanned):
        target = match.group(1).strip()
        if is_skipped_target(target):
            continue
        resolved = resolve_target(path, target)
        if not resolved.exists():
            line_no = scanned.count("\n", 0, match.start()) + 1
            issues.append(
                f"{rel}:{line_no}: broken link/image — target does not exist: {target}"
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
        print("Doc link target validation failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print(f"check-doc-link-targets: OK ({len(files)} files scanned)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
