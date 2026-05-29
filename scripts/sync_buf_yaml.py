#!/usr/bin/env python3
"""Keep buf.yaml ``modules:`` paths in sync with Contracts/Protos trees that contain .proto files."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

_SCRIPTS_DIR = Path(__file__).resolve().parent
if str(_SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPTS_DIR))

from axis_repo import BUF_CONFIG, iter_proto_module_paths

MODULES_HEADER_RE = re.compile(
    r"(?ms)^modules:\n(?:  - path: .+\n)+",
)


def read_registered_paths(text: str) -> list[str]:
    return re.findall(r"^\s+- path: (.+)$", text, re.MULTILINE)


def render_modules_block(paths: list[str]) -> str:
    lines = ["modules:"]
    lines.extend(f"  - path: {p}" for p in paths)
    lines.append("")
    return "\n".join(lines)


def apply_modules_list(text: str, paths: list[str]) -> str:
    block = render_modules_block(paths)
    if MODULES_HEADER_RE.search(text):
        return MODULES_HEADER_RE.sub(block, text, count=1)
    raise ValueError("buf.yaml: could not find modules: block to replace")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if buf.yaml modules list drifts")
    parser.add_argument("--write", action="store_true", help="rewrite buf.yaml modules list in place")
    args = parser.parse_args()

    if not BUF_CONFIG.is_file():
        print("sync-buf-yaml FAIL: buf.yaml missing", file=sys.stderr)
        return 1

    discovered = iter_proto_module_paths()
    text = BUF_CONFIG.read_text(encoding="utf-8")
    registered = read_registered_paths(text)

    if sorted(registered) == sorted(discovered):
        print(f"sync-buf-yaml: OK ({len(discovered)} proto module roots)")
        return 0

    if args.write:
        BUF_CONFIG.write_text(apply_modules_list(text, discovered), encoding="utf-8")
        print(f"sync-buf-yaml: updated buf.yaml ({len(discovered)} proto module roots)")
        return 0

    if args.check:
        print("sync-buf-yaml FAIL: buf.yaml modules: list is out of date", file=sys.stderr)
        print("  registered:", file=sys.stderr)
        for path in registered:
            print(f"    - {path}", file=sys.stderr)
        print("  discovered:", file=sys.stderr)
        for path in discovered:
            print(f"    - {path}", file=sys.stderr)
        print("  fix: python3 scripts/sync_buf_yaml.py --write", file=sys.stderr)
        return 1

    parser.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
