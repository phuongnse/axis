#!/usr/bin/env python3
"""Remove legacy US-/F-/E- spec IDs from C# and proto comments only."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCAN_ROOTS = (ROOT / "src", ROOT / "tests")

US_TOKEN = re.compile(r"US-\d{3}(?:/US-\d{3})*")
EPIC_E = re.compile(r"\bE\d{2}\b")
EPIC_F = re.compile(r"\bF\d{2}\b(?:\s+docs?)?")


def is_comment_line(line: str) -> bool:
    stripped = line.lstrip()
    return stripped.startswith("//") or stripped.startswith("///")


def clean_comment_text(body: str) -> str:
    # Parentheticals containing spec ids
    body = re.sub(r"\s*\([^)]*US-\d{3}[^)]*\)", "", body)
    body = re.sub(r"\s*\(F\d{2}\s+US-\d{3}\)", "", body)

    body = re.sub(r"\b[Pp]er\s+US-\d{3}(?:/US-\d{3})*\s*:?\s*", "", body)
    body = re.sub(r"US-\d{3}(?:/US-\d{3})*\s*:\s*", "", body)
    body = US_TOKEN.sub("", body)
    body = EPIC_E.sub("", body)
    body = re.sub(r"\bF\d{2}\s+docs?\b", "", body)
    body = EPIC_F.sub("", body)

    body = re.sub(r"\s+—\s*matches\s*$", "", body)
    body = re.sub(r"\s{2,}", " ", body)
    body = re.sub(r"\(\s*\)", "", body)
    return body.strip()


def clean_string_literal_us(text: str) -> str:
    """Strip spec ids from WithSummary/WithDescription strings."""
    if "US-" not in text and "F0" not in text:
        return text
    def repl(m: re.Match[str]) -> str:
        inner = m.group(2)
        cleaned = clean_comment_text(inner)
        return f'{m.group(1)}{cleaned}{m.group(3)}'

    return re.sub(
        r'(\.With(?:Summary|Description)\(")([^"]*)("\))',
        repl,
        text,
    )


def clean_trailing_comment(line: str) -> str:
    """Strip spec ids from end-of-line // comments."""
    idx = line.find("//")
    if idx < 0 or "US-" not in line[idx:]:
        return line
    prefix = line[: idx + 2]
    rest = line[idx + 2 :]
    cleaned = clean_comment_text(rest)
    if cleaned == rest.strip():
        return line
    suffix = "\n" if line.endswith("\n") else ""
    return f"{prefix} {cleaned}{suffix}"


def clean_line(line: str) -> str:
    if not is_comment_line(line):
        line = clean_trailing_comment(line)
        return clean_string_literal_us(line)

    # Preserve leading indent and comment marker
    match = re.match(r"^(\s*)(//+)(.*)$", line.rstrip("\n"))
    if not match:
        return line

    indent, slashes, rest = match.groups()
    suffix = "\n" if line.endswith("\n") else ""

    # XML summary: keep tags intact
    summary_open = re.match(r"^(<summary>\s*)(.*)(\s*</summary>)$", rest.strip())
    if summary_open:
        prefix, inner, suffix_tag = summary_open.groups()
        inner = clean_comment_text(inner)
        new_rest = f" {prefix}{inner}{suffix_tag}" if inner else f" {prefix}{suffix_tag}"
        return f"{indent}{slashes}{new_rest}{suffix}"

    cleaned = clean_comment_text(rest.lstrip())
    if not cleaned:
        return line  # do not blank out comments entirely

    return f"{indent}{slashes} {cleaned}{suffix}"


def process_file(path: Path) -> bool:
    original = path.read_text(encoding="utf-8")
    new_lines = [clean_line(line) for line in original.splitlines(keepends=True)]
    new_text = "".join(new_lines)
    if new_text != original:
        path.write_text(new_text, encoding="utf-8")
        return True
    return False


def main() -> int:
    changed: list[str] = []
    for root in SCAN_ROOTS:
        for path in root.rglob("*"):
            if path.suffix not in {".cs", ".proto"}:
                continue
            if process_file(path):
                changed.append(str(path.relative_to(ROOT)))
    print(f"Updated {len(changed)} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
