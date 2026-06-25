#!/usr/bin/env python3
"""Validate module/API to use-case-domain layout discovery.

Mappings are derived from the repo layout so new modules and endpoint groups
cannot silently point at a missing domain. This checker does not require an
unrelated use-case doc edit for every code change; behavioral doc accuracy is a
review checkpoint because a path-only rule cannot determine whether behavior
changed.

Run ``python3 scripts/doc_drift_domains.py --validate`` after adding a module,
endpoint group, or domain folder.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

_SCRIPTS_DIR = Path(__file__).resolve().parent
if str(_SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPTS_DIR))

from axis_repo import (  # noqa: E402
    ENDPOINTS_DIR,
    ROOT,
    USE_CASES_DIR,
    iter_module_names,
    module_to_domain_slug,
    primary_application_module,
)


def validate_discovery() -> list[str]:
    """Fail fast when module/endpoint discovery points at missing docs."""
    issues: list[str] = []

    for module in iter_module_names():
        try:
            module_to_domain_slug(module)
        except ValueError as exc:
            issues.append(str(exc))

    if not ENDPOINTS_DIR.is_dir():
        issues.append(f"missing {ENDPOINTS_DIR.relative_to(ROOT)}")
        return issues

    for endpoint_file in sorted(ENDPOINTS_DIR.glob("*Endpoints.cs")):
        try:
            module_to_domain_slug(primary_application_module(endpoint_file))
        except ValueError as exc:
            issues.append(str(exc))

    for domain_dir in sorted(USE_CASES_DIR.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith((".", "_")):
            continue
        if not (domain_dir / "README.md").is_file():
            issues.append(f"{domain_dir.relative_to(ROOT)}/README.md missing")

    return issues


def path_matches_pattern(path: str, pattern: str) -> bool:
    return re.search(pattern, path) is not None


def check_readme_api_status(changed_paths: list[str]) -> list[str]:
    """Domain README must not keep '| API | pending' after endpoint files change."""
    errors: list[str] = []
    domains_to_check: set[str] = set()
    for endpoint_file in ENDPOINTS_DIR.glob("*Endpoints.cs"):
        stem = endpoint_file.stem[: -len("Endpoints")]
        pattern = f"src/Axis\\.Api/Endpoints/{re.escape(stem)}"
        if not any(path_matches_pattern(p, pattern) for p in changed_paths):
            continue
        domains_to_check.add(module_to_domain_slug(primary_application_module(endpoint_file)))

    for domain in sorted(domains_to_check):
        readme = USE_CASES_DIR / domain / "README.md"
        if not readme.is_file():
            continue
        if re.search(r"\| API \| \u23f3", readme.read_text(encoding="utf-8")):
            errors.append(f"docs/use-cases/{domain}/README.md still has pending API status")
    return errors


def print_mappings() -> None:
    for module in iter_module_names():
        print(f"src/Modules/{module}/ -> docs/use-cases/{module_to_domain_slug(module)}/")
    for endpoint_file in sorted(ENDPOINTS_DIR.glob("*Endpoints.cs")):
        module = primary_application_module(endpoint_file)
        print(f"{endpoint_file.relative_to(ROOT)} -> docs/use-cases/{module_to_domain_slug(module)}/")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--validate",
        action="store_true",
        help="verify module/endpoint/domain discovery only (no PR diff)",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="validate discovery and check changed paths from stdin (one path per line)",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="print discovered module/API to docs mappings (debug)",
    )
    args = parser.parse_args()

    issues = validate_discovery()
    if issues:
        print("doc-drift-domains: discovery failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    if args.list:
        print_mappings()
        return 0

    if args.validate:
        print(
            f"doc-drift-domains: OK ({len(iter_module_names())} modules, "
            f"{len(list(ENDPOINTS_DIR.glob('*Endpoints.cs')))} endpoint groups)"
        )
        return 0

    if args.check:
        changed = [line.strip() for line in sys.stdin if line.strip()]
        errors = check_readme_api_status(changed)
        if errors:
            print("check-doc-domain-drift failed:", file=sys.stderr)
            for err in errors:
                print(f"  - {err}", file=sys.stderr)
            return 1
        print("check-doc-domain-drift: OK")
        return 0

    parser.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
