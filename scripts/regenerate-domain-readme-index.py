#!/usr/bin/env python3
"""Regenerate domain README Use Cases section with topical groupings."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"
SKIP = {"README.md", "_template-use-case.md"}

# domain -> list of (section title, slug prefix or callable(slug)->bool)
GROUPS: dict[str, list[tuple[str, object]]] = {
    "platform-foundation": [
        ("Registration & provisioning", lambda s: s in {
            "register-a-new-organization", "verify-email-and-activate-account",
            "automatic-tenant-provisioning", "select-a-subscription-plan-during-registration",
        }),
        ("Subscription plans", lambda s: s.startswith("view-available") or s.startswith("enforce-plan")
         or s.startswith("change-organization")),
        ("Organization settings", lambda s: "organization" in s and "plan" not in s),
        ("Tenant isolation", lambda s: "tenant" in s and s not in {
            "register-a-new-organization", "verify-email-and-activate-account",
            "automatic-tenant-provisioning", "select-a-subscription-plan-during-registration",
        }),
    ],
    "identity-access": [
        ("Authentication", lambda s: s.startswith("sign-") or "token" in s or "password" in s and "role" not in s),
        ("Users & invitations", lambda s: "user" in s or "invitation" in s),
        ("Roles & permissions", lambda s: "role" in s or "permission" in s),
        ("Localization & theming", lambda s: "switch-" in s),
    ],
    "workflow-builder": [
        ("Workflow definitions", lambda s: any(x in s for x in (
            "create-a-workflow", "view-workflows", "publish", "archive", "duplicate", "delete-a-draft",
        ))),
        ("Canvas", lambda s: any(x in s for x in ("canvas", "connect-steps", "side-panel", "navigate", "undo"))),
        ("Step types", lambda s: s.startswith("configure-") and "trigger" not in s
         and ("step" in s or "http-request" in s)),
        ("Triggers", lambda s: "trigger" in s),
        ("Branching", lambda s: "branch" in s or "if-else" in s or "merge-branches" in s),
        ("Parallel execution", lambda s: "parallel" in s or "fan-in" in s or "parallel-branches" in s),
        ("Import & export", lambda s: "export" in s or "import" in s),
    ],
    "workflow-engine": [
        ("Execution lifecycle", lambda s: any(x in s for x in (
            "start-a-workflow", "track-execution", "cancel-a-running",
        ))),
        ("History & detail", lambda s: "history" in s or "detail" in s or "timeline" in s),
        ("Errors & notifications", lambda s: "error" in s or "notification" in s),
        ("Retry", lambda s: "retry" in s),
        ("Step handlers", lambda s: "step-execution" in s),
    ],
    "form-builder": [
        ("Form definitions", lambda s: any(x in s for x in ("create-a-form", "view-all-forms", "edit-a-form", "delete-a-form", "field", "section-divider", "reorder"))),
        ("Workflow integration", lambda s: "workflow" in s or "pre-populate" in s or "map-form" in s),
        ("Submission & tasks", lambda s: any(x in s for x in ("submit", "pending", "notification", "timeout"))),
    ],
    "data-modeling": [
        ("Models", lambda s: "model" in s and "data-class" not in s),
        ("Data classes", lambda s: "data-class" in s),
        ("Records", lambda s: "record" in s or "filter-and-search" in s or "bulk" in s),
        ("Fields", lambda s: "field" in s and "form" not in s),
    ],
}


def summary_for(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    title = text.splitlines()[0].removeprefix("# Use case — ").strip()
    m = re.search(r"^## Purpose\n\n(.+)$", text, re.MULTILINE)
    desc = (m.group(1).strip() if m else title)[:100]
    if desc.startswith("_("):
        desc = title
    return f"| [{title}]({path.name}) | {desc} |"


def build_table(domain: str, files: list[Path]) -> str:
    groups = GROUPS.get(domain)
    if not groups:
        rows = [summary_for(p) for p in files]
        return "| Use case | Summary |\n|---|---|\n" + "\n".join(rows) + "\n"

    assigned: set[str] = set()
    parts: list[str] = []

    for section, predicate in groups:
        section_files = [p for p in files if predicate(p.stem) and p.stem not in assigned]
        if not section_files:
            continue
        for p in section_files:
            assigned.add(p.stem)
        parts.append(f"### {section}\n")
        parts.append("| Use case | Summary |\n|---|---|")
        parts.extend(summary_for(p) for p in sorted(section_files, key=lambda x: x.name))
        parts.append("")

    other = [p for p in files if p.stem not in assigned]
    if other:
        parts.append("### Other\n")
        parts.append("| Use case | Summary |\n|---|---|")
        parts.extend(summary_for(p) for p in sorted(other, key=lambda x: x.name))
        parts.append("")

    return "\n".join(parts) + "\n"


def regenerate(domain_dir: Path) -> None:
    readme = domain_dir / "README.md"
    if not readme.exists():
        return
    files = sorted(
        p for p in domain_dir.glob("*.md") if p.name not in SKIP
    )
    table = build_table(domain_dir.name, files)
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
        regenerate(domain_dir)
        print(f"updated {domain_dir.name}/README.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
