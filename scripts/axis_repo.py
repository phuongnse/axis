"""Shared repository layout discovery for maintenance scripts."""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODULES_DIR = ROOT / "src" / "Modules"
ENDPOINTS_DIR = ROOT / "src" / "Axis.Api" / "Endpoints"
USE_CASES_DIR = ROOT / "docs" / "use-cases"
PROGRAM_CS = ROOT / "src" / "Axis.Api" / "Program.cs"

MODULE_DOMAIN_SLUG_OVERRIDES: dict[str, str] = {
    "Identity": "identity-access",
}

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


def iter_module_names() -> list[str]:
    if not MODULES_DIR.is_dir():
        return []
    return sorted(
        p.name
        for p in MODULES_DIR.iterdir()
        if p.is_dir() and not p.name.startswith(".")
    )


def primary_application_module(endpoint_file: Path) -> str:
    text = endpoint_file.read_text(encoding="utf-8")
    for module in APPLICATION_IMPORT_RE.findall(text):
        if module != "Shared":
            return module
    raise ValueError(
        f"{endpoint_file.relative_to(ROOT)}: no Axis.{{Module}}.Application import found"
    )
