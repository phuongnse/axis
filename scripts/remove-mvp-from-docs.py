#!/usr/bin/env python3
"""Remove MVP / Phase-2 gating language from docs; normalize deferred-capability wording."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"

REPLACEMENTS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"\s*—\s*not in MVP\.?", re.IGNORECASE), "."),
    (re.compile(r"\s*—\s*not in MVP for this domain[^.]*\.?", re.IGNORECASE), "."),
    (re.compile(r"\s*not in MVP for this domain[^.]*\.?", re.IGNORECASE), ""),
    (re.compile(r"\s*\(not in MVP\)", re.IGNORECASE), ""),
    (re.compile(r"\s*not in MVP\.?", re.IGNORECASE), ""),
    (re.compile(r"\s*in MVP only", re.IGNORECASE), ""),
    (re.compile(r"\s*in MVP\b", re.IGNORECASE), ""),
    (re.compile(r"\bMVP paid plan\b", re.IGNORECASE), "Paid plan"),
    (re.compile(r"\bMVP signal\b", re.IGNORECASE), "initial signal"),
    (re.compile(r"\bfor MVP\b", re.IGNORECASE), "for production"),
    (re.compile(r"\bthe MVP\b", re.IGNORECASE), "production"),
    (re.compile(r"\bMVP loop\b", re.IGNORECASE), "core platform loop"),
    (re.compile(r"\bMVP modules\b", re.IGNORECASE), "core modules"),
    (re.compile(r"\bMVP Scope \(Phase 1\)\b"), "Production platform scope"),
    (re.compile(r"\| MVP \|"), "| Core |"),
    (re.compile(r"\*\*MVP\*\*"), "**Production**"),
    (re.compile(r"\bMVP\b"), "production"),
]

SECTION_RE = re.compile(r"^\*Out of scope\*$", re.MULTILINE)


def transform(content: str) -> str:
    for pattern, repl in REPLACEMENTS:
        content = pattern.sub(repl, content)
    content = SECTION_RE.sub("*Deferred capabilities*", content)
    # Cleanup awkward double periods / spaces (do NOT collapse `../` in relative paths)
    content = re.sub(r"  +", " ", content)
    content = re.sub(r" —\.", ".", content)
    return content


def main() -> None:
    changed: list[Path] = []
    for path in sorted(DOCS.rglob("*.md")):
        original = path.read_text(encoding="utf-8")
        updated = transform(original)
        if updated != original:
            path.write_text(updated, encoding="utf-8")
            changed.append(path)
    print(f"Updated {len(changed)} markdown files under docs/")
    print("Run scripts/repair-doc-markdown-links.py after this script to fix any broken relative paths.")


if __name__ == "__main__":
    main()
