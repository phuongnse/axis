#!/usr/bin/env python3
"""Regenerate domain README Use Cases section with topical groupings."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = {"_template", "_architecture", "_shared"}

# domain -> list of (section title, predicate on short folder name)
GROUPS: dict[str, list[tuple[str, object]]] = {
    "platform-foundation": [
        ("Registration & provisioning", lambda s: s in {
            "register-org", "verify-email", "provision-tenant", "plan-at-signup",
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


def summary_for(readme: Path) -> str:
    text = readme.read_text(encoding="utf-8")
    title = text.splitlines()[0].removeprefix("# Use case — ").strip()
    short = readme.parent.name
    m = re.search(r"^## Purpose\n\n(.+)$", text, re.MULTILINE)
    desc = (m.group(1).strip() if m else title)[:100]
    if desc.startswith("_("):
        desc = title
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


def regenerate(domain_dir: Path) -> None:
    readme = domain_dir / "README.md"
    if not readme.exists():
        return
    readmes = list_use_cases(domain_dir)
    table = build_table(domain_dir.name, readmes)
    text = readme.read_text(encoding="utf-8")
    text = re.sub(
        r"## Use Cases\n\n.*?(?=\n---\n|\n## (?!Use Cases)[A-Z]|\Z)",
        f"## Use Cases\n\n{table}\n",
        text,
        count=1,
        flags=re.DOTALL,
    )
    readme.write_text(text, encoding="utf-8")


def main() -> int:
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        if domain_dir.name == "page-builder":
            continue
        regenerate(domain_dir)
        print(f"updated {domain_dir.name}/README.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
