#!/usr/bin/env python3
"""Replace legacy US-0xx / F0N identifiers in docs with use-case names and links."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"

# Ordered replacements (longer / more specific patterns first).
REPLACEMENTS: list[tuple[str, str]] = [
    # Headings
    (r"^## US-(\d{3}) — ", r"### Use case — "),
    # Ranges and compounds
    (r"US-001–004", "register org through plan selection"),
    (r"US-010–012", "public pricing through platform admin plan change"),
    (r"US-008–009", "schema isolation and cross-tenant access"),
    (r"US-005–007", "profile, usage, and org deletion"),
    (r"US-001–004 backend", "tenant registration backend (register through plan selection)"),
    (r"US-010–012 backend", "subscription plans backend"),
    (r"US-003 backend", "[tenant provisioning](tenant-registration.md) backend"),
    (r"US-008–009 backend", "[tenant isolation](tenant-isolation.md) backend"),
    (r"US-005–007 backend", "[organization management](organization-management.md) backend"),
    (r"US-011 bulk AC", "bulk workflow import acceptance criteria in [import-export](../workflow-builder/import-export.md)"),
    (r"US-011 bulk", "bulk workflow import"),
    (r"per US-028 AC", "per [password-security](../identity-access/password-security.md) acceptance criteria"),
    (r"US-028", "password change notification"),
    (r"US-033 partial", "[model deletion guard](model-definition.md) (partial)"),
    (r"US-033", "[model deletion guard](model-definition.md)"),
    (r"US-049", "[publish workflow](workflow-definition.md)"),
    (r"US-057 edge case", "[Form step](step-types.md) deactivated-assignee edge case"),
    (r"US-057", "[Form step](step-types.md)"),
    (r"US-059", "[Condition step](step-types.md)"),
    (r"US-077", "[live-workflow warning](form-definition.md)"),
    (r"US-010 pricing page UI", "[subscription-plans](subscription-plans.md) public pricing page UI"),
    (r"US-010", "[public pricing page](subscription-plans.md)"),
    (r"US-012", "platform admin plan change"),
    (r"US-011", "bulk workflow import"),
    (r"US-008", "schema-per-tenant isolation"),
    (r"US-009", "cross-tenant access prevention"),
    (r"US-007", "[organization deletion](organization-management.md)"),
    (r"US-006", "[subscription usage settings](organization-management.md)"),
    (r"US-005", "[organization profile settings](organization-management.md)"),
    (r"US-004", "plan selection at registration"),
    (r"US-003", "[tenant provisioning](tenant-registration.md)"),
    (r"US-002", "email verification"),
    (r"US-001", "organization registration"),
    (r"US-013", "[unverified-email sign-in](authentication.md)"),
    (r"organization-management US-007", "[organization deletion](organization-management.md)"),
    (r"platform-foundation organization management US-007", "[organization deletion](../platform-foundation/organization-management.md)"),
    (r"backend US complete", "backend use cases complete"),
    (r"the next US,", "the next use case,"),
    (r"not epic checkboxes", "not domain-level checkboxes"),
    (r"per-US callouts", "per–use-case callouts"),
    (r"per US\)", "per use case)"),
    (r"per US:", "per use case:"),
    (r"AC / US \|", "AC / use case |"),
    (r"re-read the US in", "re-read the use case in"),
    (r"under the US \(", "under the use case ("),
    (r"from the US into", "from the use case into"),
    (r"exact US gaps", "exact use-case gaps"),
    (r"\[F05\]", "[password-security]"),
    (r"\[F06\]", "[localization-and-theming]"),
    (r"F06", "[parallel execution](parallel-execution.md)"),
    (r"F05\)", "password-security)"),
    (r"F05\.", "password-security."),
    (r"F05", "[password-security](password-security.md)"),
    (r"tenant-registration–F05 US", "execution monitor, retry UI, and SignalR (workflow-engine frontend)"),
    (r"tenant-registration US-002", "[tenant-registration](tenant-registration.md) email verification"),
    (r"\| F05 \|", "| page-access-control |"),
    (r"This epic", "This domain"),
    (r"this epic", "this domain"),
    (r"unrelated epic wireframes", "unrelated domain wireframes"),
    (r"an epic use-case", "a domain use-case"),
]

# Path-relative link fixes (wrong slug used as link text target).
LINK_FIXES: list[tuple[str, str, str]] = [
    ("docs/use-cases/identity-access/README.md", "[subscription-plans](permissions.md)", "[permissions](permissions.md)"),
    (
        "docs/use-cases/identity-access/README.md",
        "[organization-management](user-management.md)",
        "[user-management](user-management.md)",
    ),
    (
        "docs/use-cases/identity-access/README.md",
        "see per–use-case callouts in [tenant-registration](authentication.md)–[localization-and-theming](localization-and-theming.md).",
        "see per–use-case callouts in [authentication](authentication.md) through [localization-and-theming](localization-and-theming.md).",
    ),
    (
        "docs/use-cases/form-builder/README.md",
        "[subscription-plans](form-submission.md)",
        "[form-submission](form-submission.md)",
    ),
    (
        "docs/use-cases/data-modeling/README.md",
        "[subscription-plans](data-records.md)",
        "[data-records](data-records.md)",
    ),
    (
        "docs/use-cases/workflow-engine/README.md",
        "[tenant-registration](execution-management.md)",
        "[execution-management](execution-management.md)",
    ),
    (
        "docs/use-cases/workflow-engine/README.md",
        "[tenant-isolation](error-handling.md)",
        "[error-handling](error-handling.md)",
    ),
    (
        "docs/use-cases/workflow-engine/README.md",
        "[organization-management](step-handlers.md)",
        "[step-handlers](step-handlers.md)",
    ),
    (
        "docs/use-cases/workflow-engine/README.md",
        "[subscription-plans](execution-history.md)",
        "[execution-history](execution-history.md)",
    ),
    (
        "docs/playbooks/patterns.md",
        "[tenant-registration](../identity-access/authentication.md)",
        "[authentication](../use-cases/identity-access/authentication.md)",
    ),
]

GENERATE_SCREENS_REPLACEMENTS: list[tuple[str, str]] = [
    ("US-001 / US-005", "tenant-registration / organization profile"),
    ("US-001, US-004", "tenant-registration, plan selection"),
    ("US-001 validation", "tenant-registration validation"),
    ("US-001 success state, US-002 resend", "registration success, email verification resend"),
    ("Resend link (US-002)", "Resend link (email verification)"),
    ("US-002 (all 4 outcome states)", "email verification (all 4 outcome states)"),
    ("US-003 (2 states side by side)", "tenant provisioning (2 states side by side)"),
    ("US-007", "organization deletion"),
    ("US-004 (plan selection before registration), US-010 (public pricing page)", "plan selection at registration, public pricing page"),
    ("US-005, US-006, US-007", "profile, usage, organization deletion"),
    ("US-005 inline validation", "profile settings inline validation"),
    ("US-006 edge case", "usage settings edge case"),
    ("US-006 — non-admin", "usage settings — non-admin"),
    ("US-002 (tenant-registration) / US-013", "email verification (tenant-registration) / unverified sign-in"),
]

PAGE_BUILDER_TABLE = """| Use case | Description |
|---|---|
| page-management | Create, edit, delete, publish/unpublish pages |
| widget-library | Pre-built widgets: List, Grid, Form, Chart, Button, Text, Image |
| layout-builder | Compose pages by dragging widgets onto a canvas, resize and arrange |
| data-binding | Bind widget data sources to models, records, and workflow outputs |
| page-access-control | Control which roles can access each page |"""


def apply_replacements(text: str, patterns: list[tuple[str, str]], *, multiline: bool = False) -> str:
    for old, new in patterns:
        if old.startswith("^") or multiline:
            text = re.sub(old, new, text, flags=re.MULTILINE)
        else:
            text = text.replace(old, new)
    return text


def process_markdown(path: Path) -> bool:
    original = path.read_text(encoding="utf-8")
    updated = apply_replacements(original, REPLACEMENTS, multiline=True)
    rel = str(path.relative_to(ROOT)).replace("\\", "/")
    for file_key, old, new in LINK_FIXES:
        if rel == file_key.replace("\\", "/"):
            updated = updated.replace(old, new)
    if path.name == "README.md" and "page-builder" in rel:
        if "| F05 | Page Access Control |" in updated or "| tenant-registration | Page Management |" in updated:
            updated = re.sub(
                r"\| Use case \| Description \|\n\|---\|---\|\n(?:\|[^\n]+\n)+",
                PAGE_BUILDER_TABLE + "\n",
                updated,
                count=1,
            )
    if updated != original:
        path.write_text(updated, encoding="utf-8")
        return True
    return False


def process_generate_screens() -> bool:
    path = DOCS / "use-cases/_shared/wireframes/generate-screens.mjs"
    original = path.read_text(encoding="utf-8")
    updated = apply_replacements(original, GENERATE_SCREENS_REPLACEMENTS)
    if updated != original:
        path.write_text(updated, encoding="utf-8")
        return True
    return False


def main() -> int:
    changed: list[str] = []
    for path in sorted(DOCS.rglob("*")):
        if path.suffix in {".md", ".mjs"} and path.is_file():
            if process_markdown(path) if path.suffix == ".md" else False:
                changed.append(str(path.relative_to(ROOT)))
    if process_generate_screens():
        changed.append("docs/use-cases/_shared/wireframes/generate-screens.mjs")
    # Playbooks outside docs tree
    for rel in ("docs/playbooks/patterns.md", "docs/playbooks/testing.md", "docs/playbooks/agent-checklist.md", "docs/WORKAROUNDS.md"):
        p = ROOT / rel
        if p.exists() and process_markdown(p):
            changed.append(rel)
    if changed:
        print(f"Updated {len(changed)} files:")
        for c in changed:
            print(f"  {c}")
    else:
        print("No files changed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
