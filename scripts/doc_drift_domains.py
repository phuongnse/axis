#!/usr/bin/env python3
"""Discover code-path → use-case domain mappings for doc-drift checks.

Mappings are derived from the repo layout, not a hand-maintained list:

  - ``src/Modules/{Module}/`` → ``docs/use-cases/{domain}/`` (PascalCase → kebab-case,
    with a small override table for names that do not match the folder slug).
  - ``src/Axis.Api/Endpoints/*Endpoints.cs`` → same domain as the module referenced
    by ``using Axis.{Module}.Application`` (first non-Shared Application import).

Extra rules (cross-cutting paths that are not module-scoped) live in
``EXTRA_CODE_TO_DOC_RULES`` — keep that list minimal.

Run ``python3 scripts/doc_drift_domains.py --validate`` after adding a module,
endpoint group, or domain folder. Agent checklists: docs/playbooks/repo-layout-discovery.md
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

_SCRIPTS_DIR = Path(__file__).resolve().parent
if str(_SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPTS_DIR))

from axis_repo import (
    ENDPOINTS_DIR,
    MODULES_DIR,
    ROOT,
    USE_CASES_DIR,
    iter_module_names,
    module_to_domain_slug,
    primary_application_module,
)

# Cross-cutting paths: (code regex, doc prefix under repo root, label).
EXTRA_CODE_TO_DOC_RULES: list[tuple[str, str, str]] = [
    (
        r"src/Modules/.*/.*OrganizationVerifiedHandler",
        "docs/use-cases/platform-foundation",
        "platform-foundation tenant provisioning",
    ),
    (
        r"frontend/src/(features/auth|routes/|components/layout/AppShell)",
        "docs/use-cases/identity-access",
        "identity-access auth frontend",
    ),
]


@dataclass
class DomainDriftRule:
    """Require docs under doc_prefix when any code_pattern matches a changed path."""

    doc_prefix: str
    label: str
    code_patterns: list[str] = field(default_factory=list)


def discover_rules() -> list[DomainDriftRule]:
    """Build merged rules (one per doc_prefix) from modules, endpoints, and extras."""
    by_doc: dict[str, DomainDriftRule] = {}

    def add_pattern(doc_prefix: str, label: str, pattern: str) -> None:
        rule = by_doc.get(doc_prefix)
        if rule is None:
            rule = DomainDriftRule(doc_prefix=doc_prefix, label=label, code_patterns=[])
            by_doc[doc_prefix] = rule
        if pattern not in rule.code_patterns:
            rule.code_patterns.append(pattern)

    for module in iter_module_names():
        domain = module_to_domain_slug(module)
        doc_prefix = f"docs/use-cases/{domain}"
        add_pattern(
            doc_prefix,
            f"{domain} module",
            f"src/Modules/{module}/",
        )

    for endpoint_file in sorted(ENDPOINTS_DIR.glob("*Endpoints.cs")):
        module = primary_application_module(endpoint_file)
        domain = module_to_domain_slug(module)
        doc_prefix = f"docs/use-cases/{domain}"
        stem = endpoint_file.stem[: -len("Endpoints")]
        add_pattern(
            doc_prefix,
            f"{domain} API",
            f"src/Axis\\.Api/Endpoints/{re.escape(stem)}",
        )

    for pattern, doc_prefix, label in EXTRA_CODE_TO_DOC_RULES:
        add_pattern(doc_prefix, label, pattern)

    return sorted(by_doc.values(), key=lambda r: r.doc_prefix)


def validate_discovery() -> list[str]:
    """Fail fast when the tree and mapping table are out of sync."""
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
            primary_application_module(endpoint_file)
        except ValueError as exc:
            issues.append(str(exc))

    for domain_dir in sorted(USE_CASES_DIR.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        if domain_dir.name == "page-builder":
            continue
        if not (domain_dir / "README.md").is_file():
            issues.append(f"{domain_dir.relative_to(ROOT)}/README.md missing")

    return issues


def path_matches_pattern(path: str, pattern: str) -> bool:
    return re.search(pattern, path) is not None


def is_csproj_only_match(changed_paths: list[str], pattern: str) -> bool:
    """True when every changed path matching pattern is a .csproj (Dependabot noise)."""
    matches = [p for p in changed_paths if path_matches_pattern(p, pattern)]
    if not matches:
        return False
    return all(p.endswith(".csproj") for p in matches)


def check_domain_docs(changed_paths: list[str], rules: list[DomainDriftRule]) -> list[str]:
    errors: list[str] = []
    for rule in rules:
        triggered = False
        for pattern in rule.code_patterns:
            if not any(path_matches_pattern(p, pattern) for p in changed_paths):
                continue
            if is_csproj_only_match(changed_paths, pattern):
                continue
            triggered = True
            break
        if not triggered:
            continue
        doc_prefix = rule.doc_prefix + "/"
        if not any(p.startswith(doc_prefix) for p in changed_paths):
            errors.append(
                f"{rule.label}: code changed but no files under {rule.doc_prefix}/ in this PR"
            )
    return errors


def check_readme_api_status(changed_paths: list[str]) -> list[str]:
    """Domain README must not keep '| API | ⏳' after API endpoint files change."""
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
        if re.search(r"\| API \| ⏳", readme.read_text(encoding="utf-8")):
            errors.append(
                f"docs/use-cases/{domain}/README.md still '| API | ⏳' — set ⚠️ or ✅"
            )
    return errors


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
        help="print discovered rules (debug)",
    )
    args = parser.parse_args()

    issues = validate_discovery()
    if issues:
        print("doc-drift-domains: discovery failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    rules = discover_rules()

    if args.list:
        for rule in rules:
            print(f"{rule.doc_prefix}  ({rule.label})")
            for pat in rule.code_patterns:
                print(f"  {pat}")
        return 0

    if args.validate:
        print(
            f"doc-drift-domains: OK ({len(iter_module_names())} modules, "
            f"{len(list(ENDPOINTS_DIR.glob('*Endpoints.cs')))} endpoint groups, "
            f"{len(rules)} doc rules)"
        )
        return 0

    if args.check:
        changed = [line.strip() for line in sys.stdin if line.strip()]
        errors = check_domain_docs(changed, rules)
        errors.extend(check_readme_api_status(changed))
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
