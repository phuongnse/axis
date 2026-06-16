"""Shared repository layout discovery for maintenance scripts.

Agent playbook (checklists, auto vs manual): docs/playbooks/repo-layout-discovery.md
"""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODULES_DIR = ROOT / "src" / "Modules"
ENDPOINTS_DIR = ROOT / "src" / "Axis.Api" / "Endpoints"
USE_CASES_DIR = ROOT / "docs" / "use-cases"
BUF_CONFIG = ROOT / "buf.yaml"
PROGRAM_CS = ROOT / "src" / "Axis.Api" / "Program.cs"

MODULE_DOMAIN_SLUG_OVERRIDES: dict[str, str] = {
    "Identity": "identity-access",
}

KAFKA_TOPIC_CONST_RE = re.compile(
    r'public\s+const\s+string\s+\w+\s*=\s*"(axis\.[^"]+)";',
    re.MULTILINE,
)

APPLICATION_IMPORT_RE = re.compile(
    r"^using\s+Axis\.([A-Za-z0-9]+)\.Application\b",
    re.MULTILINE,
)


def pascal_to_kebab(name: str) -> str:
    return re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", name).lower()


def module_to_domain_slug(module_name: str) -> str:
    slug = MODULE_DOMAIN_SLUG_OVERRIDES.get(module_name, pascal_to_kebab(module_name))
    domain_dir = USE_CASES_DIR / slug
    if not domain_dir.is_dir():
        raise ValueError(
            f"module {module_name!r} maps to docs/use-cases/{slug} but that folder is missing"
        )
    return slug


def module_to_tenant_slug(module_name: str) -> str:
    """Tenant provisioning id (``TenantModuleNames`` values)."""
    return module_name.lower()


def iter_module_names() -> list[str]:
    if not MODULES_DIR.is_dir():
        return []
    return sorted(
        p.name
        for p in MODULES_DIR.iterdir()
        if p.is_dir() and not p.name.startswith(".")
    )


def iter_proto_module_paths() -> list[str]:
    """Paths listed under buf.yaml ``modules:`` — one per Contracts/Protos tree."""
    paths: list[str] = []
    for protos in sorted(MODULES_DIR.glob("*/Axis.*.Contracts/Protos")):
        if any(protos.glob("**/*.proto")):
            paths.append(str(protos.relative_to(ROOT)).replace("\\", "/"))
    return paths


def iter_kafka_topics() -> list[str]:
    topics: list[str] = []
    for topics_file in sorted(MODULES_DIR.glob("*/Axis.*.Contracts/*KafkaTopics.cs")):
        text = topics_file.read_text(encoding="utf-8")
        topics.extend(KAFKA_TOPIC_CONST_RE.findall(text))
    return sorted(set(topics))


def modules_with_team_account_verified_handler() -> list[str]:
    """Modules that provision tenant schema on TeamAccountVerifiedEvent."""
    found: list[str] = []
    for module in iter_module_names():
        if module == "Identity":
            continue
        if list(MODULES_DIR.glob(f"{module}/**/TeamAccountVerifiedHandler.cs")):
            found.append(module)
    return sorted(found)


def primary_application_module(endpoint_file: Path) -> str:
    text = endpoint_file.read_text(encoding="utf-8")
    for module in APPLICATION_IMPORT_RE.findall(text):
        if module != "Shared":
            return module
    raise ValueError(
        f"{endpoint_file.relative_to(ROOT)}: no Axis.{{Module}}.Application import found"
    )
