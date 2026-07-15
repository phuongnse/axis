"""Shared repository layout discovery for maintenance scripts."""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parent.parent
MODULES_DIR = ROOT / "src" / "Modules"
ENDPOINTS_DIR = ROOT / "src" / "Axis.Api" / "Endpoints"
USE_CASES_DIR = ROOT / "docs" / "use-cases"
PROGRAM_CS = ROOT / "src" / "Axis.Api" / "Program.cs"

MODULE_DOMAIN_SLUG_OVERRIDES: dict[str, str] = {
    "Identity": "identity-access",
}

FALLBACK_GENERATED_OR_DEPENDENCY_DIRS = {"bin", "node_modules", "obj"}
MODULE_SOURCE_SUFFIXES = {".cs", ".csproj"}

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


def git_visible_paths_under(path: Path) -> list[Path] | None:
    try:
        relative_path = path.relative_to(ROOT)
    except ValueError:
        return None

    try:
        result = subprocess.run(
            [
                "git",
                "-C",
                str(ROOT),
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "--",
                str(relative_path),
            ],
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return None

    return [ROOT / line for line in result.stdout.splitlines() if line]


def iter_files(root: Path, suffixes: tuple[str, ...]) -> Iterable[Path]:
    if not root.exists():
        return []

    visible_paths = git_visible_paths_under(root)
    if visible_paths is not None:
        return (
            path
            for path in visible_paths
            if path.is_file() and path.suffix in suffixes
        )

    return (
        path
        for path in root.rglob("*")
        if path.is_file()
        and path.suffix in suffixes
        and not any(part in FALLBACK_GENERATED_OR_DEPENDENCY_DIRS for part in path.parts)
    )


def fallback_has_module_source(module_dir: Path) -> bool:
    for _, dir_names, file_names in os.walk(module_dir):
        dir_names[:] = [
            name
            for name in dir_names
            if name not in FALLBACK_GENERATED_OR_DEPENDENCY_DIRS and not name.startswith(".")
        ]
        if any(Path(file_name).suffix in MODULE_SOURCE_SUFFIXES for file_name in file_names):
            return True
    return False


def has_module_source(module_dir: Path) -> bool:
    visible_paths = git_visible_paths_under(module_dir)
    if visible_paths is None:
        return fallback_has_module_source(module_dir)
    return any(path.is_file() and path.suffix in MODULE_SOURCE_SUFFIXES for path in visible_paths)


def iter_module_names() -> list[str]:
    if not MODULES_DIR.is_dir():
        return []
    return sorted(
        p.name
        for p in MODULES_DIR.iterdir()
        if (
            p.is_dir()
            and not p.name.startswith(".")
            and p.name not in FALLBACK_GENERATED_OR_DEPENDENCY_DIRS
            and has_module_source(p)
        )
    )


def primary_application_module(endpoint_file: Path) -> str:
    text = endpoint_file.read_text(encoding="utf-8")
    for module in APPLICATION_IMPORT_RE.findall(text):
        if module != "Shared":
            return module
    raise ValueError(
        f"{endpoint_file.relative_to(ROOT)}: no Axis.{{Module}}.Application import found"
    )
