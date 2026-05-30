#!/usr/bin/env python3
"""Remove legacy Excalidraw diagram tables from use-case READMEs.

Replaced by docs-style practice: Mermaid under ## Diagrams, or omit the section.

Usage:
  python3 scripts/migrate_legacy_diagram_tables.py --check
  python3 scripts/migrate_legacy_diagram_tables.py --apply
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
USE_CASES = ROOT / "docs" / "use-cases"

# Stock N/A diagram table shipped before Mermaid migration.
LEGACY_NA_DIAGRAMS_BLOCK = re.compile(
    r"\n## Diagrams\n\n"
    r"\| Diagram \| Source \| Preview \|\n"
    r"\|[-:| ]+\|\n"
    r"\| N/A \| N/A \| N/A \|\n?",
    re.MULTILINE,
)

LEGACY_DIAGRAM_TABLE_HEADER = "| Diagram | Source | Preview |"


def iter_readmes() -> list[Path]:
    files: list[Path] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir():
            continue
        for readme in sorted(domain_dir.glob("*/README.md")):
            files.append(readme)
    return files


def strip_legacy_blocks(text: str) -> tuple[str, int]:
    count = 0
    while True:
        updated, n = LEGACY_NA_DIAGRAMS_BLOCK.subn("\n", text, count=1)
        if n == 0:
            break
        text = updated
        count += 1
    return text, count


def main() -> int:
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--check", action="store_true")
    group.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    changed: list[Path] = []
    remaining_legacy: list[Path] = []

    for path in iter_readmes():
        text = path.read_text(encoding="utf-8")
        if LEGACY_DIAGRAM_TABLE_HEADER in text:
            remaining_legacy.append(path)
        new_text, blocks_removed = strip_legacy_blocks(text)
        if blocks_removed and new_text != text:
            changed.append(path)
            if args.apply:
                path.write_text(new_text, encoding="utf-8")

    if args.check:
        if remaining_legacy:
            rels = ", ".join(p.relative_to(ROOT).as_posix() for p in remaining_legacy[:8])
            more = len(remaining_legacy) - 8
            suffix = f" (+{more} more)" if more > 0 else ""
            print(
                f"migrate_legacy_diagram_tables: {len(remaining_legacy)} file(s) still have "
                f"legacy diagram table(s): {rels}{suffix}",
                file=sys.stderr,
            )
            return 1
        print("migrate_legacy_diagram_tables: OK (no legacy diagram tables)")
        return 0

    print(f"migrate_legacy_diagram_tables: updated {len(changed)} file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
