#!/usr/bin/env python3
"""Restore markdown link targets from main when the MVP cleanup script broke relative paths."""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"

LINK_RE = re.compile(r"(\[[^\]]*\]\()([^)]+)(\))")


def git_show_main(rel_path: str) -> str | None:
    result = subprocess.run(
        ["git", "show", f"main:{rel_path}"],
        capture_output=True,
        text=True,
        cwd=ROOT,
        check=False,
    )
    if result.returncode != 0:
        return None
    return result.stdout


def link_target_exists(from_file: Path, url: str) -> bool:
    if url.startswith(("http://", "https://", "mailto:")):
        return True
    path_part = url.split("#", 1)[0]
    if not path_part or path_part.endswith("/"):
        return (from_file.parent / path_part).exists()
    return (from_file.parent / path_part).exists()


def extract_urls(content: str) -> list[str]:
    return [match.group(2) for match in LINK_RE.finditer(content)]


def restore_links(from_file: Path, current: str, main: str) -> str:
    main_urls = extract_urls(main)
    cur_matches = list(LINK_RE.finditer(current))
    if len(main_urls) != len(cur_matches):
        return current

    main_iter = iter(main_urls)

    def replace(match: re.Match[str]) -> str:
        cur_url = match.group(2)
        main_url = next(main_iter)
        if cur_url == main_url:
            return match.group(0)
        if link_target_exists(from_file, cur_url):
            return match.group(0)
        if link_target_exists(from_file, main_url):
            return f"{match.group(1)}{main_url}{match.group(3)}"
        return match.group(0)

    return LINK_RE.sub(replace, current)


def repair_dot_collapses(content: str) -> str:
    while "././" in content:
        content = content.replace("././", "../")
    return content


def main() -> None:
    changed = 0
    for path in sorted(DOCS.rglob("*.md")):
        rel = path.relative_to(ROOT).as_posix()
        original = path.read_text(encoding="utf-8")
        updated = repair_dot_collapses(original)
        main_content = git_show_main(rel)
        if main_content is not None:
            updated = restore_links(path, updated, main_content)
        if updated != original:
            path.write_text(updated, encoding="utf-8")
            changed += 1
    print(f"Repaired markdown links in {changed} files")


if __name__ == "__main__":
    main()
