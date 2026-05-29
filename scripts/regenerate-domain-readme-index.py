#!/usr/bin/env python3
"""Regenerate domain README Use Cases section with topical groupings.

Usage:
  python3 scripts/regenerate-domain-readme-index.py          # rewrite domain READMEs
  python3 scripts/regenerate-domain-readme-index.py --check  # CI: fail if tables drift
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = set()

# domain -> list of (section title, predicate on short folder name)
GROUPS: dict[str, list[tuple[str, object]]] = {
    "platform-foundation": [
        ("Registration & provisioning", lambda s: s in {
            "register-org", "provision-tenant", "plan-at-signup",
        }),
        ("Subscription plans", lambda s: s in {"view-plans", "enforce-limits", "admin-change-plan"}),
        ("Organization settings", lambda s: s in {"org-profile", "org-settings", "delete-org"}),
        ("Tenant isolation", lambda s: s in {"tenant-scope", "tenant-from-jwt"}),
    ],
    "identity-access": [
        ("Authentication", lambda s: s in {
            "sign-in", "token-refresh", "sign-out", "reset-password", "change-password", "sessions",
        }),
        ("Users & invitations", lambda s: s in {
            "invite-user", "accept-invite", "deactivate-user", "user-profile",
        }),
        ("Roles & permissions", lambda s: s in {
            "list-roles", "create-role", "edit-role", "assign-role", "api-permissions", "ui-permissions",
        }),
        ("Localization & theming", lambda s: s in {"language", "theme"}),
    ],
    "workflow-builder": [
        ("Workflow definitions", lambda s: s in {
            "create-workflow", "list-workflows", "publish-workflow", "archive-workflow",
            "duplicate-workflow", "delete-draft",
        }),
        ("Canvas", lambda s: s in {
            "add-canvas-step", "connect-steps", "step-side-panel", "canvas-nav", "canvas-undo",
        }),
        ("Step types", lambda s: s in {
            "form-step", "http-step", "condition-step", "script-step", "notification-step",
        }),
        ("Triggers", lambda s: s in {
            "manual-trigger", "schedule-trigger", "webhook-trigger", "event-trigger",
        }),
        ("Branching", lambda s: s in {"if-else-branch", "multi-branch", "merge-branches"}),
        ("Parallel execution", lambda s: s in {"parallel-group", "fan-in-join", "parallel-results"}),
        ("Import & export", lambda s: s in {"export-json", "import-json", "bulk-export"}),
    ],
    "workflow-engine": [
        ("Execution lifecycle", lambda s: s in {"start-execution", "track-execution", "cancel-execution"}),
        ("History & detail", lambda s: s in {
            "workflow-history", "execution-detail", "org-execution-history",
        }),
        ("Errors & notifications", lambda s: s in {"error-detail", "failure-notify", "error-channels"}),
        ("Retry", lambda s: s in {"retry-execution", "retry-history", "retry-with-context"}),
        ("Step handlers", lambda s: s == "isolated-steps"),
    ],
    "form-builder": [
        ("Form definitions", lambda s: s in {
            "create-form", "list-forms", "edit-form", "delete-form", "add-form-field",
            "form-field-validation", "reorder-form-fields", "section-divider",
        }),
        ("Workflow integration", lambda s: s in {"link-form-step", "prepopulate-fields", "map-submission-context"}),
        ("Submission & tasks", lambda s: s in {
            "assignment-notify", "submit-assigned-form", "pending-tasks", "form-timeout",
        }),
    ],
    "data-modeling": [
        ("Models", lambda s: s in {"create-model", "list-models", "edit-model", "delete-model", "add-field", "field-validation", "reorder-fields"}),
        ("Data classes", lambda s: "data-class" in s),
        ("Records", lambda s: s in {"create-record", "list-records", "search-records", "edit-record", "delete-record", "bulk-records"}),
    ],
}


SUMMARY_MAX = 120


def _truncate_at_word(text: str, limit: int) -> str:
    if len(text) <= limit:
        return text
    cut = text[:limit]
    # Trim back to last whole-word boundary so we never split mid-token.
    last_space = cut.rfind(" ")
    if last_space > limit // 2:
        cut = cut[:last_space]
    return cut.rstrip(" ,.;:") + "…"


def summary_for(readme: Path) -> str:
    text = readme.read_text(encoding="utf-8")
    title = text.splitlines()[0].removeprefix("# Use case — ").strip()
    short = readme.parent.name
    m = re.search(r"^## Purpose\n\n(.+)$", text, re.MULTILINE)
    desc = (m.group(1).strip() if m else title)
    if desc.startswith("_("):
        desc = title
    desc = _truncate_at_word(desc, SUMMARY_MAX)
    return f"| [{title}]({short}/) | {desc} |"


def list_use_cases(domain_dir: Path) -> list[Path]:
    return sorted(
        p / "README.md"
        for p in domain_dir.iterdir()
        if p.is_dir() and p.name not in SKIP_DIRS and (p / "README.md").exists()
    )


def build_table(domain: str, readmes: list[Path]) -> str:
    groups = GROUPS.get(domain)
    if not groups:
        rows = [summary_for(p) for p in readmes]
        return "| Use case | Summary |\n|---|---|\n" + "\n".join(rows) + "\n"

    assigned: set[str] = set()
    parts: list[str] = []

    for section, predicate in groups:
        section_files = [p for p in readmes if predicate(p.parent.name) and p.parent.name not in assigned]
        if not section_files:
            continue
        for p in section_files:
            assigned.add(p.parent.name)
        parts.append(f"### {section}\n")
        parts.append("| Use case | Summary |\n|---|---|")
        parts.extend(summary_for(p) for p in sorted(section_files, key=lambda p: p.parent.name))
        parts.append("")

    other = [p for p in readmes if p.parent.name not in assigned]
    if other:
        parts.append("### Other\n")
        parts.append("| Use case | Summary |\n|---|---|")
        parts.extend(summary_for(p) for p in sorted(other, key=lambda p: p.parent.name))
        parts.append("")

    return "\n".join(parts) + "\n"


USE_CASES_SECTION_RE = re.compile(
    r"## Use Cases\n\n.*?(?=\n---\n|\n## (?!Use Cases)[A-Z]|\Z)",
    re.DOTALL,
)


def expected_use_cases_section(domain_dir: Path) -> str | None:
    readme = domain_dir / "README.md"
    if not readme.exists():
        return None
    readmes = list_use_cases(domain_dir)
    table = build_table(domain_dir.name, readmes)
    return f"## Use Cases\n\n{table}\n"


def apply_use_cases_section(readme: Path, expected: str) -> None:
    text = readme.read_text(encoding="utf-8")
    text = USE_CASES_SECTION_RE.sub(expected, text, count=1)
    readme.write_text(text, encoding="utf-8")


def check_domain(domain_dir: Path) -> list[str]:
    readme = domain_dir / "README.md"
    expected = expected_use_cases_section(domain_dir)
    if expected is None:
        return []
    text = readme.read_text(encoding="utf-8")
    match = USE_CASES_SECTION_RE.search(text)
    actual = match.group(0) if match else ""
    if actual != expected:
        rel = readme.relative_to(ROOT)
        return [
            f"{rel}: ## Use Cases table out of date — run "
            f"python3 scripts/regenerate-domain-readme-index.py"
        ]
    return []


def regenerate(domain_dir: Path) -> None:
    expected = expected_use_cases_section(domain_dir)
    if expected is None:
        return
    apply_use_cases_section(domain_dir / "README.md", expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail when domain README Use Cases tables are out of date",
    )
    args = parser.parse_args()

    issues: list[str] = []
    updated = 0
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        if domain_dir.name == "page-builder":
            continue
        if args.check:
            issues.extend(check_domain(domain_dir))
        else:
            regenerate(domain_dir)
            print(f"updated {domain_dir.name}/README.md")
            updated += 1

    if args.check:
        if issues:
            print("regenerate-domain-readme-index: FAIL", file=sys.stderr)
            for issue in issues:
                print(f"  - {issue}", file=sys.stderr)
            return 1
        print("regenerate-domain-readme-index: OK")
        return 0

    print(f"regenerate-domain-readme-index: updated {updated} domain README(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
