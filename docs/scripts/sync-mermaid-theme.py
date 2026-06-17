#!/usr/bin/env python3
"""Ensure every Mermaid block in docs/ starts with the canonical init line."""

from __future__ import annotations

import re
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DOCS_ROOT = SCRIPT_DIR.parent
REPO_ROOT = DOCS_ROOT.parent

sys.path.insert(0, str(REPO_ROOT))
from docs.diagrams.mermaid_theme import MERMAID_INIT  # noqa: E402

SKIP_FILES = {"playbooks/mermaid.md"}
INIT_RE = re.compile(r"%%\{init:[\s\S]*?\}%%[ \t]*(?:\r?\n[ \t]*)*")
FENCE_RE = re.compile(r"```mermaid\n([\s\S]*?)```")


def sync_mermaid_blocks(content: str) -> tuple[str, bool]:
    changed = False

    def replace(match: re.Match[str]) -> str:
        nonlocal changed
        body = match.group(1)
        stripped = body.lstrip()
        if INIT_RE.search(stripped):
            without_init = INIT_RE.sub("", stripped, count=1)
            next_body = f"{MERMAID_INIT}\n{without_init.lstrip(chr(10))}"
        else:
            next_body = f"{MERMAID_INIT}\n{body.lstrip(chr(10))}"

        if next_body != body:
            changed = True
        return f"```mermaid\n{next_body}```"

    return FENCE_RE.sub(replace, content), changed


def write_lf(path: Path, content: str) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def main() -> int:
    updated = 0
    for path in DOCS_ROOT.rglob("*.md"):
        if any(part.startswith(".") for part in path.relative_to(DOCS_ROOT).parts):
            continue
        rel = path.relative_to(DOCS_ROOT).as_posix()
        if rel in SKIP_FILES:
            continue

        raw = path.read_text(encoding="utf-8")
        content, changed = sync_mermaid_blocks(raw)
        if changed:
            write_lf(path, content)
            print(f"updated docs/{rel}")
            updated += 1

    print(f"Done. {updated} file(s)." if updated else "All mermaid blocks already synced.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
