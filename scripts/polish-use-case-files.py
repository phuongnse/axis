#!/usr/bin/env python3
"""Normalize split use-case files: one UC per file, no migration junk, scoped wireframes."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = {"README.md", "_template", "_architecture", "_shared"}

H1_RE = re.compile(r"^# Use case — (.+)$", re.MULTILINE)
UC_HEADING_RE = re.compile(r"^### Use case — (.+)$", re.MULTILINE)
STORY_RE = re.compile(
    r"\*\*As an?\*\*\s+([^,]+),\s+\*\*I want(?:\s+to)?\*\*\s+(.+?)\s+\*\*so that\*\*\s+(.+?)\.\s*",
    re.DOTALL,
)
MIGRATION_BLOCK_RE = re.compile(
    r"\*\*Purpose:\*\* _\(to be detailed during migration\)_\s*"
    r"\*\*Primary actor:\*\* _\(to be detailed during migration\)_\s*"
    r"\*\*Trigger:\*\* _\(to be detailed during migration\)_\s*"
    r"#### Main flow\s*1\. _\(to be detailed during migration\)_\s*"
    r"#### Alternate / error flows\s*- _\(to be detailed during migration\)_\s*",
    re.MULTILINE,
)
PLACEHOLDER_MAIN = re.compile(
    r"## Main flow\n\n1\. _\(Happy path — align with acceptance criteria below\.\)_\n",
    re.MULTILINE,
)
IMPL_STATUS_RE = re.compile(
    r"(> \*\*Implementation status\*\*.*?)(?=\n---\n|\n## |\Z)",
    re.DOTALL,
)

# slug -> wireframe base names (without extension)
WIREFRAMES: dict[str, list[str]] = {
    # platform-foundation
    "register-a-new-organization": ["register-org", "register-org-states", "email-confirmation"],
    "verify-email-and-activate-account": [
        "verify-email",
        "verify-email-rate-limit",
        "../identity-access/wireframes/login-unverified",
    ],
    "automatic-tenant-provisioning": ["workspace-provisioning"],
    "select-a-subscription-plan-during-registration": ["pricing"],
    "view-available-plans": ["pricing"],
    "update-organization-profile": ["settings-org", "settings-org-profile-states", "settings-org-upload-states"],
    "view-organization-settings": [
        "settings-org",
        "settings-org-usage-error",
        "settings-org-free-plan",
        "settings-org-access-denied",
    ],
    "delete-organization": [
        "settings-org-delete-modal",
        "settings-org-delete-states",
        "settings-org-deletion-scheduled",
    ],
    "change-organization-plan-admin-override": ["pricing", "settings-org"],
    # identity-access
    "sign-in-with-email-and-password": ["login"],
    "reset-forgotten-password": ["forgot-password"],
    "change-password-while-signed-in": ["change-password"],
    "view-and-revoke-active-sessions": ["settings-security"],
    "invite-a-user-to-the-organization": ["settings-users"],
    "accept-an-invitation": ["accept-invitation"],
    "deactivate-a-user": ["settings-users"],
    "manage-user-profile": ["settings-users"],
    "view-and-manage-roles": ["settings-roles"],
    "create-a-custom-role": ["settings-roles"],
    "edit-a-custom-role": ["settings-roles"],
    "assign-a-role-to-a-user": ["settings-roles", "settings-users"],
    # data-modeling
    "create-a-model": ["data-models"],
    "view-all-models": ["data-models"],
    "edit-a-model": ["data-models"],
    "delete-a-model": ["data-models"],
    "create-a-data-class": ["data-classes"],
    "edit-a-data-class": ["data-classes"],
    "delete-a-data-class": ["data-classes"],
    "use-a-data-class-as-a-field-in-a-model": ["data-classes", "data-models"],
    "add-a-field-to-a-model": ["data-models"],
    "configure-field-validation-rules": ["data-models"],
    "reorder-fields": ["data-models"],
    "create-a-record": ["records"],
    "view-records-list": ["records"],
    "filter-and-search-records": ["records"],
    "edit-a-record": ["records"],
    "delete-a-record": ["records"],
    "bulk-operations-on-records": ["records"],
    # form-builder
    "create-a-form": ["forms"],
    "view-all-forms": ["forms"],
    "edit-a-form": ["form-editor"],
    "delete-a-form": ["forms"],
    "add-a-field-to-a-form": ["form-editor"],
    "configure-validation-rules-on-a-field": ["form-editor"],
    "reorder-fields-via-drag-and-drop": ["form-editor"],
    "add-a-section-divider": ["form-editor"],
    "link-a-form-to-a-workflow-form-step": ["form-editor", "workflow-builder/wireframes/workflow-editor"],
    "open-and-submit-an-assigned-form": ["form-submission"],
    "view-pending-form-tasks": ["form-submission"],
    "receive-form-assignment-notification": ["form-submission"],
    "handle-form-step-timeout": ["form-submission"],
    # workflow-builder
    "create-a-workflow": ["workflows"],
    "view-workflows-list": ["workflows"],
    "publish-a-workflow": ["workflow-editor"],
    "archive-a-workflow": ["workflows"],
    "duplicate-a-workflow": ["workflows"],
    "delete-a-draft-workflow": ["workflows"],
    "add-a-step-to-the-canvas": ["workflow-editor"],
    "connect-steps-with-transitions": ["workflow-editor"],
    "configure-a-step-via-side-panel": ["workflow-editor"],
    "navigate-and-zoom-the-canvas": ["workflow-editor"],
    "undo-and-redo-canvas-actions": ["workflow-editor"],
    "configure-a-form-step": ["workflow-editor"],
    "configure-an-http-request-step": ["workflow-editor"],
    "configure-a-condition-step": ["workflow-editor"],
    "configure-a-script-step": ["workflow-editor"],
    "configure-a-notification-step": ["workflow-editor"],
    "configure-a-manual-trigger": ["workflow-editor"],
    "configure-a-schedule-trigger": ["workflow-editor"],
    "configure-a-webhook-trigger": ["workflow-editor"],
    "configure-an-event-trigger": ["workflow-editor"],
    "add-an-if-else-branch": ["workflow-editor"],
    "add-a-multi-branch-condition": ["workflow-editor"],
    "merge-branches-back-to-a-single-path": ["workflow-editor"],
    "create-a-parallel-step-group": ["workflow-editor"],
    "configure-fan-in-join-behavior": ["workflow-editor"],
    "access-results-from-parallel-branches": ["workflow-editor"],
    "export-a-workflow-as-json": ["workflows"],
    "import-a-workflow-from-json": ["workflows"],
    "bulk-export-all-workflows": ["workflows"],
    # workflow-engine
    "start-a-workflow-execution": ["executions"],
    "track-execution-status-in-real-time": ["executions", "execution-detail"],
    "cancel-a-running-execution": ["execution-detail"],
    "view-execution-history-for-a-workflow": ["executions"],
    "view-execution-detail-and-step-timeline": ["execution-detail"],
    "view-org-wide-execution-history": ["executions"],
    "view-detailed-error-information": ["execution-detail"],
    "receive-error-notification-when-a-workflow-fails": ["execution-detail"],
    "configure-error-notification-channels-per-workflow": ["workflow-editor"],
    "retry-a-failed-execution": ["execution-detail"],
    "view-retry-history": ["execution-detail"],
    "retry-with-modified-input-context": ["execution-detail"],
    "step-execution-is-isolated-and-resilient": ["execution-detail"],
}

DOMAIN_DEFAULT_WIREFRAME: dict[str, str] = {
    "workflow-builder": "workflow-editor",
    "workflow-engine": "executions",
    "form-builder": "forms",
    "data-modeling": "data-models",
    "identity-access": "login",
    "platform-foundation": "settings-org",
}


def title_matches(a: str, b: str) -> bool:
    return a.strip().casefold() == b.strip().casefold()


def extract_section_for_title(text: str, title: str) -> str | None:
    matches = list(UC_HEADING_RE.finditer(text))
    for index, match in enumerate(matches):
        if not title_matches(match.group(1), title):
            continue
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        return text[start:end].strip()
    return None


def extract_acceptance_block(text: str) -> str:
    """Pull AC + implementation status from body."""
    text = MIGRATION_BLOCK_RE.sub("", text)
    # Drop duplicate nested use-case sections
    first_nested = UC_HEADING_RE.search(text)
    if first_nested:
        text = text[: first_nested.start()].strip()

    # Find last implementation status and include everything before wireframes/diagrams from AC onward
    impl_matches = list(IMPL_STATUS_RE.finditer(text))
    if impl_matches:
        impl = impl_matches[-1].group(1).strip()
    else:
        impl = (
            "> **Implementation status**\n>\n> | Layer | Status |\n> |-------|--------|\n"
            "> | Domain | ⏳ |\n> | Application | ⏳ |\n> | Infrastructure | ⏳ |\n"
            "> | API | ⏳ |\n> | Frontend | ⏳ |\n>\n> **Gaps vs spec:** TBD.\n"
        )

    ac_start = re.search(r"(\*\*Acceptance Criteria:\*\*|## Acceptance Criteria)", text)
    if ac_start:
        ac_text = text[ac_start.start() :]
        # Trim at implementation status
        impl_pos = ac_text.find("> **Implementation status**")
        if impl_pos >= 0:
            ac_part = ac_text[:impl_pos].strip()
        else:
            ac_part = ac_text.strip()
    else:
        ac_part = ""

    ac_part = re.sub(r"^\*\*Acceptance Criteria:\*\*\s*", "", ac_part, flags=re.MULTILINE)
    ac_part = re.sub(r"^## Acceptance Criteria\s*", "", ac_part, flags=re.MULTILINE)
    ac_part = ac_part.strip()
    if ac_part.startswith("**Acceptance Criteria:**"):
        ac_part = ac_part[len("**Acceptance Criteria:**") :].strip()

    return f"## Acceptance Criteria\n\n{ac_part}\n\n{impl}".strip()


def capitalize_purpose(sentence: str) -> str:
    sentence = sentence.strip()
    if not sentence:
        return "_(One sentence about user value.)_"
    if sentence[0].islower():
        sentence = sentence[0].upper() + sentence[1:]
    if not sentence.endswith("."):
        sentence += "."
    return sentence


def wireframe_table(domain: str, slug: str) -> str:
    names = WIREFRAMES.get(slug)
    if not names:
        default = DOMAIN_DEFAULT_WIREFRAME.get(domain)
        names = [default] if default else []

    if not names:
        return (
            "| Screen | Excalidraw | Preview |\n|--------|------------|---------|\n"
            "| _(none — API or backend only)_ | N/A | N/A |"
        )

    rows: list[str] = []
    for name in names:
        if name.startswith("../"):
            base = name
        elif "/" in name:
            base = f"../{name}"
        else:
            base = f"./wireframes/{name}"
        rows.append(
            f"| {Path(name).name} | [source]({base}.excalidraw) | [preview]({base}.svg) |"
        )
    header = "| Screen | Excalidraw | Preview |\n|--------|------------|---------|"
    return header + "\n" + "\n".join(rows)


def extract_context(text: str) -> str:
    match = re.search(r"^## Context\n\n(.*?)(?=\n---\n|\n## |\Z)", text, re.MULTILINE | re.DOTALL)
    if not match:
        return ""
    body = match.group(1).strip()
    if not body or "to be detailed" in body:
        return ""
    return f"## Context\n\n{body}\n"


def polish_file(path: Path) -> bool:
    original = path.read_text(encoding="utf-8")
    h1 = H1_RE.search(original)
    if not h1:
        return False

    title = h1.group(1).strip()
    domain = path.parent.name
    slug = path.stem

    section = extract_section_for_title(original, title)
    body_source = section if section else original

    story = STORY_RE.search(body_source)
    if story:
        actor, want, goal = story.groups()
        purpose = capitalize_purpose(f"{want.strip()} so that {goal.strip()}")
        actor_line = f"- {actor.strip()}"
        trigger_line = f"- {want.strip().capitalize()}."
    else:
        purpose_match = re.search(r"^## Purpose\n\n(.+)$", original, re.MULTILINE)
        purpose = capitalize_purpose(purpose_match.group(1)) if purpose_match else "_(TBD)_"
        actor_match = re.search(r"^## Primary actor\n\n(.+)$", original, re.MULTILINE)
        actor_line = actor_match.group(1).strip() if actor_match else "- _(TBD)_"
        trigger_match = re.search(r"^## Trigger\n\n(.+)$", original, re.MULTILINE)
        trigger_line = trigger_match.group(1).strip() if trigger_match else "- _(TBD)_"

    context = extract_context(original)
    ac_block = extract_acceptance_block(body_source if section else original)

    main_flow = (
        "## Main flow\n\n"
        "1. Actor satisfies the trigger.\n"
        "2. System performs the happy-path steps in Acceptance Criteria.\n"
        "3. Actor receives the expected outcome.\n"
    )
    alt_flow = (
        "## Alternate / error flows\n\n"
        "- Validation failures and edge cases in Acceptance Criteria.\n"
    )

    new_text = f"""# Use case — {title}

> **Navigation**: [← {domain.replace('-', ' ').title()}](./README.md)

## Purpose

{purpose}

## Primary actor

{actor_line}

## Trigger

{trigger_line}

{main_flow}
{alt_flow}
{context}
{ac_block}

## Wireframes

{wireframe_table(domain, slug)}

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
"""

    if new_text != original:
        path.write_text(new_text, encoding="utf-8")
        return True
    return False


def main() -> int:
    changed = 0
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        for path in sorted(domain_dir.glob("*/README.md")):
            if path.parent.name in SKIP_DIRS:
                continue
            if polish_file(path):
                changed += 1
    print(f"Polished {changed} use-case files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
