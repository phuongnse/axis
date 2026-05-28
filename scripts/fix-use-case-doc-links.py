#!/usr/bin/env python3
"""Rewrite links from flat use-case .md paths to folder paths."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_short_slug() -> dict[str, str]:
    import importlib.util

    spec = importlib.util.spec_from_file_location(
        "migrate_uc", ROOT / "scripts" / "migrate-use-case-folders.py"
    )
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.SHORT_SLUG


SHORT_SLUG = load_short_slug()


def replacements() -> list[tuple[str, str]]:
    pairs: list[tuple[str, str]] = []
    for long, short in SHORT_SLUG.items():
        pairs.append((f"{long}.md", f"{short}/"))
        pairs.append((f"/{long}.md", f"/{short}/"))
    return pairs


def fix_text(text: str) -> str:
    for old, new in replacements():
        text = text.replace(old, new)
    # normalize bulk-export path
    text = text.replace("workflow-builder/import-json/", "workflow-builder/import-json/")
    text = text.replace(
        "bulk workflow import acceptance criteria in [import-export](../workflow-builder/import-json/)",
        "bulk workflow import acceptance criteria in [import-json](../../workflow-builder/import-json/)",
    )
    return text


def main() -> int:
    reps = replacements()
    changed = 0
    for path in ROOT.rglob("*"):
        if path.suffix not in {".md", ".sh", ".mjs", ".ps1"}:
            continue
        if "node_modules" in path.parts or ".git" in path.parts:
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        if not any(old in text for old, _ in reps):
            continue
        new_text = fix_text(text)
        if new_text != text:
            path.write_text(new_text, encoding="utf-8")
            changed += 1
    print(f"Updated links in {changed} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
