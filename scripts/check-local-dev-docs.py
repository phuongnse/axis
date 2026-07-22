#!/usr/bin/env python3
"""Verify docs/playbooks/local-dev.md matches local Docker Compose.

Usage:
  python3 scripts/check-local-dev-docs.py          # validate (exit 0/1)
  python3 scripts/check-local-dev-docs.py --check  # same (CI alias)
"""

from __future__ import annotations

import argparse
import ast
import json
import re
import shlex
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MAIN_COMPOSE_FILE = ROOT / "docker-compose.yml"
LOCAL_DEV_FILE = ROOT / "docs/playbooks/local-dev.md"
TECH_STACK_FILE = ROOT / "docs/TECH_STACK.md"
API_APPSETTINGS_FILE = ROOT / "src" / "Axis.Api" / "appsettings.json"
LOCAL_BROWSER_APP_BASE_URL = "https://localhost:3000"

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
LOCAL_APP_BASE_URL_LINE = re.compile(
    rf'^\s+App__BaseUrl:\s+"(?:[$]{{APP_BASE_URL:-)?{re.escape(LOCAL_BROWSER_APP_BASE_URL)}(?:}})?"\s*$',
    re.MULTILINE,
)


def mentions_service(doc: str, service_name: str) -> bool:
    service_pattern = re.compile(rf"(?<![a-z0-9_-]){re.escape(service_name)}(?![a-z0-9_-])")
    return service_pattern.search(doc.lower()) is not None


def services_section(text: str) -> str:
    lines = text.splitlines(keepends=True)
    body: list[str] = []
    in_services = False
    for line in lines:
        if not in_services:
            if line.startswith("services:"):
                in_services = True
            continue
        if line.strip() and not line.startswith((" ", "\t")):
            break
        body.append(line)
    return "".join(body)


def parse_compose(compose_file: Path) -> tuple[dict[str, list[int]], set[str], list[str]]:
    text = compose_file.read_text(encoding="utf-8")
    service_text = services_section(text)
    services: dict[str, list[int]] = {}
    optional_services: set[str] = set()
    service_names: list[str] = []

    for match in SERVICE_BLOCK.finditer(service_text):
        name = match.group(1)
        block = match.group(2)
        if name == "volumes":
            continue

        service_names.append(name)

        profile_match = PROFILE_LINE.search(block)
        if profile_match:
            optional_services.add(name)

        ports = [int(default or literal) for default, literal in PORT_MAPPING.findall(block)]
        if ports:
            services[name] = ports

    return services, optional_services, sorted(service_names)


def mandatory_host_ports(services: dict[str, list[int]], optional: set[str]) -> set[int]:
    ports: set[int] = set()
    for name, mapped in services.items():
        if name in optional:
            continue
        for host_port in mapped:
            ports.add(host_port)
    return ports


def compose_has_local_app_base_url(compose_file: Path) -> bool:
    return LOCAL_APP_BASE_URL_LINE.search(compose_file.read_text(encoding="utf-8")) is not None


def service_property(block: str, property_name: str) -> str:
    lines = block.splitlines()
    marker = re.compile(rf"^    {re.escape(property_name)}:\s*(.*)$")

    for index, line in enumerate(lines):
        match = marker.match(line)
        if match is None:
            continue

        value = [match.group(1)] if match.group(1) else []
        for nested_line in lines[index + 1 :]:
            if nested_line.startswith("      ") or not nested_line.strip():
                value.append(nested_line.strip())
                continue
            break
        return "\n".join(value)

    return ""


def compose_command_tokens(command: str) -> list[str]:
    value = command.strip()
    if not value:
        return []

    if value.startswith("["):
        if not value.endswith("]"):
            return []
        lexer = shlex.shlex(value[1:-1], posix=True)
        lexer.whitespace += ","
        lexer.whitespace_split = True
        lexer.commenters = ""
        try:
            return list(lexer)
        except ValueError:
            return []

    lines = [line.strip() for line in value.splitlines() if line.strip()]
    if lines and all(line.startswith("-") for line in lines):
        tokens: list[str] = []
        for line in lines:
            scalar = line.removeprefix("-").strip()
            if scalar.startswith(("'", '"')):
                try:
                    scalar = ast.literal_eval(scalar)
                except (SyntaxError, ValueError):
                    return []
            if not isinstance(scalar, str):
                return []
            tokens.append(scalar)
        return tokens

    try:
        return shlex.split(value)
    except ValueError:
        return []


def api_has_source_reload(compose_file: Path) -> bool:
    service_text = services_section(compose_file.read_text(encoding="utf-8"))
    api_block = next(
        (match.group(2) for match in SERVICE_BLOCK.finditer(service_text) if match.group(1) == "api"),
        "",
    )
    command_tokens = compose_command_tokens(service_property(api_block, "command"))
    has_watcher = [token.lower() for token in command_tokens[:2]] == ["dotnet", "watch"]
    polling_environment = service_property(api_block, "environment")
    has_bind_mount_polling = re.search(
        r'^DOTNET_USE_POLLING_FILE_WATCHER:\s*["\']?(?:1|true)["\']?$',
        polling_environment,
        re.IGNORECASE | re.MULTILINE,
    ) is not None
    return has_watcher and has_bind_mount_polling


def api_appsettings_base_url(appsettings_file: Path) -> str | None:
    try:
        data = json.loads(appsettings_file.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return None

    app_section = data.get("App")
    if not isinstance(app_section, dict):
        return None

    base_url = app_section.get("BaseUrl")
    return base_url if isinstance(base_url, str) else None


def check_local_dev_doc() -> list[str]:
    errors: list[str] = []

    if not MAIN_COMPOSE_FILE.is_file():
        errors.append(f"Missing {MAIN_COMPOSE_FILE.relative_to(ROOT)}")
    if not LOCAL_DEV_FILE.is_file():
        errors.append(f"Missing {LOCAL_DEV_FILE.relative_to(ROOT)}")
    if not API_APPSETTINGS_FILE.is_file():
        errors.append(f"Missing {API_APPSETTINGS_FILE.relative_to(ROOT)}")
    if errors:
        return errors

    main_services, main_optional, service_names = parse_compose(MAIN_COMPOSE_FILE)
    doc = LOCAL_DEV_FILE.read_text(encoding="utf-8")

    doc_lower = doc.lower()

    if "scripts/axis.py doctor" not in doc:
        errors.append("local-dev.md should document the local environment doctor command")

    if ".env.local" not in doc:
        errors.append("local-dev.md should document the ignored local env file")

    if "package-manager adapter" not in doc_lower or "binary/shim" not in doc_lower:
        errors.append("local-dev.md should document the generic package-manager adapter")

    if not compose_has_local_app_base_url(MAIN_COMPOSE_FILE):
        errors.append(
            "docker-compose.yml api service must set App__BaseUrl to "
            f"{LOCAL_BROWSER_APP_BASE_URL} for human local-dev verification links"
        )

    if not api_has_source_reload(MAIN_COMPOSE_FILE):
        errors.append("docker-compose.yml api service must automatically reload source changes")

    if api_appsettings_base_url(API_APPSETTINGS_FILE) != LOCAL_BROWSER_APP_BASE_URL:
        errors.append(
            "src/Axis.Api/appsettings.json App:BaseUrl must default to "
            f"{LOCAL_BROWSER_APP_BASE_URL} for host-native local dev"
        )

    if (
        "App:BaseUrl" not in doc
        or LOCAL_BROWSER_APP_BASE_URL not in doc
        or "browser-facing origin" not in doc_lower
    ):
        errors.append(
            "local-dev.md should document App:BaseUrl as the browser-facing origin "
            "used in verification email links"
        )

    for host_port in sorted(mandatory_host_ports(main_services, main_optional)):
        if str(host_port) not in doc:
            errors.append(
                f"local-dev.md missing host port {host_port} "
                f"(published by docker-compose.yml)"
            )

    for service_name in service_names:
        if not mentions_service(doc, service_name):
            errors.append(
                f"local-dev.md missing service name '{service_name}' "
                f"(defined in docker-compose.yml)"
            )

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
