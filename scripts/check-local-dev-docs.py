#!/usr/bin/env python3
"""Verify docs/playbooks/local-dev.md matches docker-compose.yml.

Usage:
  python3 scripts/check-local-dev-docs.py          # validate (exit 0/1)
  python3 scripts/check-local-dev-docs.py --check  # same (CI alias)
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
COMPOSE_FILE = ROOT / "docker-compose.yml"
LOCAL_DEV_FILE = ROOT / "docs/playbooks/local-dev.md"
TECH_STACK_FILE = ROOT / "docs/TECH_STACK.md"

TECH_STACK_FRAGMENT_LINK = re.compile(r"\]\(\.\./TECH_STACK\.md#([^)]+)\)")
TECH_STACK_KNOWN_ANCHOR = re.compile(r"\[[^\]]+\]\(#([^)]+)\)")

SERVICE_BLOCK = re.compile(
    r"^  ([a-z0-9_-]+):\n((?:    .*\n)*)",
    re.MULTILINE,
)
PORT_MAPPING = re.compile(r'^\s+-\s+"(\d+):\d+"', re.MULTILINE)
PROFILE_LINE = re.compile(r'^\s+profiles:\s*\[(.+)\]', re.MULTILINE)

STALE_MARKERS = [
    ("EnsureCreated", "Identity bootstrap uses MigrateAsync — remove EnsureCreated references"),
]

def parse_compose() -> tuple[dict[str, list[int]], set[str], list[str]]:
    text = COMPOSE_FILE.read_text(encoding="utf-8")
    services: dict[str, list[int]] = {}
    optional_services: set[str] = set()

    for match in SERVICE_BLOCK.finditer(text):
        name = match.group(1)
        block = match.group(2)
        if name == "volumes":
            continue

        profile_match = PROFILE_LINE.search(block)
        if profile_match:
            optional_services.add(name)

        ports = [int(port) for port in PORT_MAPPING.findall(block)]
        if ports:
            services[name] = ports

    service_names = sorted(services.keys())
    return services, optional_services, service_names


def mandatory_host_ports(services: dict[str, list[int]], optional: set[str]) -> set[int]:
    ports: set[int] = set()
    for name, mapped in services.items():
        if name in optional:
            continue
        for host_port in mapped:
            ports.add(host_port)
    return ports


def check_local_dev_doc() -> list[str]:
    errors: list[str] = []

    if not COMPOSE_FILE.is_file():
        errors.append(f"Missing {COMPOSE_FILE.relative_to(ROOT)}")
        return errors
    if not LOCAL_DEV_FILE.is_file():
        errors.append(f"Missing {LOCAL_DEV_FILE.relative_to(ROOT)}")
        return errors

    services, optional, service_names = parse_compose()
    doc = LOCAL_DEV_FILE.read_text(encoding="utf-8")

    doc_lower = doc.lower()

    if "scripts/axis.py doctor" not in doc:
        errors.append("local-dev.md should document the local environment doctor command")

    if "npm.cmd" not in doc:
        errors.append("local-dev.md should document using npm.cmd from Windows PowerShell")

    for host_port in sorted(mandatory_host_ports(services, optional)):
        if str(host_port) not in doc:
            errors.append(
                f"local-dev.md missing host port {host_port} "
                f"(published by docker-compose.yml)"
            )

    for service_name in service_names:
        if service_name not in doc_lower:
            errors.append(
                f"local-dev.md missing service name '{service_name}' "
                f"(defined in docker-compose.yml)"
            )

    if "observability" not in doc_lower:
        errors.append("local-dev.md missing observability profile documentation")

    for stale, message in STALE_MARKERS:
        if stale in doc:
            errors.append(f"local-dev.md stale content: {message}")

    if "MigrateAsync" not in doc:
        errors.append("local-dev.md should document Identity MigrateAsync dev bootstrap")

    if "AutoProvision" not in doc:
        errors.append("local-dev.md should mention Wolverine AutoProvision in Development")

    if TECH_STACK_FILE.is_file():
        tech_stack_text = TECH_STACK_FILE.read_text(encoding="utf-8")
        known_anchors = set(TECH_STACK_KNOWN_ANCHOR.findall(tech_stack_text))
        for fragment in TECH_STACK_FRAGMENT_LINK.findall(doc):
            if fragment not in known_anchors:
                errors.append(
                    f"local-dev.md broken TECH_STACK link fragment #{fragment} "
                    f"(not found in {TECH_STACK_FILE.relative_to(ROOT)})"
                )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="CI alias — validate and exit non-zero on drift",
    )
    _args = parser.parse_args()

    errors = check_local_dev_doc()
    if errors:
        print("check-local-dev-docs FAIL:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        print(
            f"\nUpdate {LOCAL_DEV_FILE.relative_to(ROOT)} to match "
            f"{COMPOSE_FILE.relative_to(ROOT)}.",
            file=sys.stderr,
        )
        return 1

    print("check-local-dev-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
