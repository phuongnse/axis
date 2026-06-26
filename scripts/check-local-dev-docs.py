#!/usr/bin/env python3
"""Verify docs/playbooks/local-dev.md matches local Docker Compose.

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
MAIN_COMPOSE_FILE = ROOT / "docker-compose.yml"
OPEN_DESIGN_COMPOSE_FILE = ROOT / "docker-compose.open-design.yml"
COMPOSE_FILES = (MAIN_COMPOSE_FILE, OPEN_DESIGN_COMPOSE_FILE)
LOCAL_DEV_FILE = ROOT / "docs/playbooks/local-dev.md"
TECH_STACK_FILE = ROOT / "docs/TECH_STACK.md"

TECH_STACK_FRAGMENT_LINK = re.compile(r"\]\(\.\./TECH_STACK\.md#([^)]+)\)")
TECH_STACK_KNOWN_ANCHOR = re.compile(r"\[[^\]]+\]\(#([^)]+)\)")

SERVICE_BLOCK = re.compile(
    r"^  ([a-z0-9_-]+):\n((?:    .*\n)*)",
    re.MULTILINE,
)
PORT_MAPPING = re.compile(
    r'^\s+-\s+"(?:127[.]0[.]0[.]1:)?(?:[$][{][A-Z0-9_]+:-(\d+)[}]|(\d+)):\d+"',
    re.MULTILINE,
)
PROFILE_LINE = re.compile(r'^\s+profiles:\s*\[(.+)\]', re.MULTILINE)


def mentions_service(doc: str, service_name: str) -> bool:
    service_pattern = re.compile(rf"(?<![a-z0-9_-]){re.escape(service_name)}(?![a-z0-9_-])")
    return service_pattern.search(doc.lower()) is not None


def parse_compose(compose_file: Path) -> tuple[dict[str, list[int]], set[str], list[str]]:
    text = compose_file.read_text(encoding="utf-8")
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

        ports = [int(default or literal) for default, literal in PORT_MAPPING.findall(block)]
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

    for compose_file in COMPOSE_FILES:
        if not compose_file.is_file():
            errors.append(f"Missing {compose_file.relative_to(ROOT)}")
    if not LOCAL_DEV_FILE.is_file():
        errors.append(f"Missing {LOCAL_DEV_FILE.relative_to(ROOT)}")
    if errors:
        return errors

    main_services, main_optional, _main_service_names = parse_compose(MAIN_COMPOSE_FILE)
    open_design_services, open_design_optional, _open_design_service_names = parse_compose(OPEN_DESIGN_COMPOSE_FILE)
    service_names = sorted({*main_services.keys(), *open_design_services.keys()})
    doc = LOCAL_DEV_FILE.read_text(encoding="utf-8")

    doc_lower = doc.lower()

    if "scripts/axis.py doctor" not in doc:
        errors.append("local-dev.md should document the local environment doctor command")

    if ".env.local" not in doc:
        errors.append("local-dev.md should document the ignored local env file")

    if "package-manager adapter" not in doc_lower or "binary/shim" not in doc_lower:
        errors.append("local-dev.md should document the generic package-manager adapter")

    for host_port in sorted(mandatory_host_ports(main_services, main_optional)):
        if str(host_port) not in doc:
            errors.append(
                f"local-dev.md missing host port {host_port} "
                f"(published by docker-compose.yml)"
            )

    for host_port in sorted(mandatory_host_ports(open_design_services, open_design_optional)):
        if str(host_port) not in doc:
            errors.append(
                f"local-dev.md missing host port {host_port} "
                f"(published by docker-compose.open-design.yml)"
            )

    for service_name in service_names:
        if not mentions_service(doc, service_name):
            errors.append(
                f"local-dev.md missing service name '{service_name}' "
                f"(defined in local compose files)"
            )

    if "docker-compose.open-design.yml" not in doc or "open-design up" not in doc:
        errors.append("local-dev.md missing Docker Open Design workflow")

    if "observability" not in doc_lower:
        errors.append("local-dev.md missing observability profile documentation")

    if "MigrateAsync" not in doc:
        errors.append("local-dev.md should document Identity MigrateAsync database startup")

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
            f"\nUpdate {LOCAL_DEV_FILE.relative_to(ROOT)} to match local compose files.",
            file=sys.stderr,
        )
        return 1

    print("check-local-dev-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
