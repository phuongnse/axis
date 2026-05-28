#!/usr/bin/env python3
"""Move each use case into docs/use-cases/<domain>/<short-slug>/README.md."""

from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"

# long filename (stem) -> short folder name
SHORT_SLUG: dict[str, str] = {
    # platform-foundation
    "register-a-new-organization": "register-org",
    "verify-email-and-activate-account": "verify-email",
    "automatic-tenant-provisioning": "provision-tenant",
    "select-a-subscription-plan-during-registration": "plan-at-signup",
    "view-available-plans": "view-plans",
    "enforce-plan-limits-at-the-api": "enforce-limits",
    "change-organization-plan-admin-override": "admin-change-plan",
    "update-organization-profile": "org-profile",
    "view-organization-settings": "org-settings",
    "delete-organization": "delete-org",
    "automatic-tenant-scoping-on-every-request": "tenant-scope",
    "tenant-resolution-from-jwt": "tenant-from-jwt",
    # identity-access
    "sign-in-with-email-and-password": "sign-in",
    "silent-token-refresh": "token-refresh",
    "sign-out": "sign-out",
    "reset-forgotten-password": "reset-password",
    "change-password-while-signed-in": "change-password",
    "view-and-revoke-active-sessions": "sessions",
    "invite-a-user-to-the-organization": "invite-user",
    "accept-an-invitation": "accept-invite",
    "deactivate-a-user": "deactivate-user",
    "manage-user-profile": "user-profile",
    "view-and-manage-roles": "list-roles",
    "create-a-custom-role": "create-role",
    "edit-a-custom-role": "edit-role",
    "assign-a-role-to-a-user": "assign-role",
    "permission-enforcement-on-the-api": "api-permissions",
    "permission-enforcement-in-the-frontend": "ui-permissions",
    "switch-application-language-english-vietnamese": "language",
    "switch-visual-theme-light-dark-system": "theme",
    # data-modeling
    "create-a-model": "create-model",
    "view-all-models": "list-models",
    "edit-a-model": "edit-model",
    "delete-a-model": "delete-model",
    "create-a-data-class": "create-data-class",
    "edit-a-data-class": "edit-data-class",
    "delete-a-data-class": "delete-data-class",
    "use-a-data-class-as-a-field-in-a-model": "data-class-field",
    "add-a-field-to-a-model": "add-field",
    "configure-field-validation-rules": "field-validation",
    "reorder-fields": "reorder-fields",
    "create-a-record": "create-record",
    "view-records-list": "list-records",
    "filter-and-search-records": "search-records",
    "edit-a-record": "edit-record",
    "delete-a-record": "delete-record",
    "bulk-operations-on-records": "bulk-records",
    # form-builder
    "create-a-form": "create-form",
    "view-all-forms": "list-forms",
    "edit-a-form": "edit-form",
    "delete-a-form": "delete-form",
    "add-a-field-to-a-form": "add-form-field",
    "configure-validation-rules-on-a-field": "form-field-validation",
    "reorder-fields-via-drag-and-drop": "reorder-form-fields",
    "add-a-section-divider": "section-divider",
    "link-a-form-to-a-workflow-form-step": "link-form-step",
    "pre-populate-form-fields-from-execution-context": "prepopulate-fields",
    "map-form-submission-data-into-workflow-context": "map-submission-context",
    "receive-form-assignment-notification": "assignment-notify",
    "open-and-submit-an-assigned-form": "submit-assigned-form",
    "view-pending-form-tasks": "pending-tasks",
    "handle-form-step-timeout": "form-timeout",
    # workflow-builder
    "create-a-workflow": "create-workflow",
    "view-workflows-list": "list-workflows",
    "publish-a-workflow": "publish-workflow",
    "archive-a-workflow": "archive-workflow",
    "duplicate-a-workflow": "duplicate-workflow",
    "delete-a-draft-workflow": "delete-draft",
    "add-a-step-to-the-canvas": "add-canvas-step",
    "connect-steps-with-transitions": "connect-steps",
    "configure-a-step-via-side-panel": "step-side-panel",
    "navigate-and-zoom-the-canvas": "canvas-nav",
    "undo-and-redo-canvas-actions": "canvas-undo",
    "configure-a-form-step": "form-step",
    "configure-an-http-request-step": "http-step",
    "configure-a-condition-step": "condition-step",
    "configure-a-script-step": "script-step",
    "configure-a-notification-step": "notification-step",
    "configure-a-manual-trigger": "manual-trigger",
    "configure-a-schedule-trigger": "schedule-trigger",
    "configure-a-webhook-trigger": "webhook-trigger",
    "configure-an-event-trigger": "event-trigger",
    "add-an-if-else-branch": "if-else-branch",
    "add-a-multi-branch-condition": "multi-branch",
    "merge-branches-back-to-a-single-path": "merge-branches",
    "create-a-parallel-step-group": "parallel-group",
    "configure-fan-in-join-behavior": "fan-in-join",
    "access-results-from-parallel-branches": "parallel-results",
    "export-a-workflow-as-json": "export-json",
    "import-a-workflow-from-json": "import-json",
    "bulk-export-all-workflows": "bulk-export",
    # workflow-engine
    "start-a-workflow-execution": "start-execution",
    "track-execution-status-in-real-time": "track-execution",
    "cancel-a-running-execution": "cancel-execution",
    "view-execution-history-for-a-workflow": "workflow-history",
    "view-execution-detail-and-step-timeline": "execution-detail",
    "view-org-wide-execution-history": "org-execution-history",
    "view-detailed-error-information": "error-detail",
    "receive-error-notification-when-a-workflow-fails": "failure-notify",
    "configure-error-notification-channels-per-workflow": "error-channels",
    "retry-a-failed-execution": "retry-execution",
    "view-retry-history": "retry-history",
    "retry-with-modified-input-context": "retry-with-context",
    "step-execution-is-isolated-and-resilient": "isolated-steps",
}

# Wireframes used by exactly one use case -> copy into use-case folder
EXCLUSIVE_WIREFRAMES: dict[str, list[str]] = {
    "register-org": ["register-org", "register-org-states", "email-confirmation"],
    "verify-email": ["verify-email", "verify-email-rate-limit"],
    "provision-tenant": ["workspace-provisioning"],
    "plan-at-signup": ["pricing"],
    "view-plans": ["pricing"],
    "org-profile": ["settings-org-profile-states", "settings-org-upload-states"],
    "org-settings": [
        "settings-org",
        "settings-org-usage-error",
        "settings-org-free-plan",
        "settings-org-access-denied",
    ],
    "delete-org": [
        "settings-org-delete-modal",
        "settings-org-delete-states",
        "settings-org-deletion-scheduled",
    ],
    "sign-in": ["login"],
    "reset-password": ["forgot-password"],
    "change-password": ["change-password"],
    "sessions": ["settings-security"],
    "invite-user": ["settings-users"],
    "accept-invite": ["accept-invitation"],
    "user-profile": ["settings-users"],
    "list-roles": ["settings-roles"],
    "create-role": ["settings-roles"],
    "edit-role": ["settings-roles"],
    "assign-role": ["settings-roles", "settings-users"],
}


def rewrite_content(text: str, domain: str, short: str) -> str:
    domain_title = domain.replace("-", " ").title()
    text = re.sub(
        r"> \*\*Navigation\*\*: \[← [^\]]+\]\(\./README\.md\)",
        f"> **Navigation**: [← {domain_title}](../README.md) · [Use cases index](../README.md#use-cases)",
        text,
    )
    # domain wireframes -> ../wireframes/ when not copied locally
    text = text.replace(f"](./wireframes/", f"](../wireframes/")
    text = text.replace(f"](../{domain}/wireframes/", f"](../wireframes/")
    # cross-domain wireframes
    text = re.sub(
        r"\[source\]\(\.\./identity-access/wireframes/",
        r"[source](../../identity-access/wireframes/",
        text,
    )
    text = re.sub(
        r"\[preview\]\(\.\./identity-access/wireframes/",
        r"[preview](../../identity-access/wireframes/",
        text,
    )
    text = re.sub(
        r"\[source\]\(\.\./workflow-builder/wireframes/",
        r"[source](../../workflow-builder/wireframes/",
        text,
    )
    text = re.sub(
        r"\[preview\]\(\.\./workflow-builder/wireframes/",
        r"[preview](../../workflow-builder/wireframes/",
        text,
    )
    # links to other use cases in same domain (old flat .md)
    for long, short_name in SHORT_SLUG.items():
        text = text.replace(f"]({long}.md)", f"](../{short_name}/)")
        text = text.replace(f"](../{domain}/{long}.md)", f"](../{short_name}/)")
        text = text.replace(f"](../{long}.md)", f"](../{short_name}/)")
    text = text.replace("../workflow-builder/README.md", "../../workflow-builder/")
    text = text.replace("../workflow-builder/bulk-export", "../../workflow-builder/bulk-export")
    return text


def copy_wireframes(domain_dir: Path, short: str, names: list[str]) -> None:
    wf_dir = domain_dir / short / "wireframes"
    wf_dir.mkdir(parents=True, exist_ok=True)
    src_root = domain_dir / "wireframes"
    for name in names:
        for ext in (".excalidraw", ".svg"):
            src = src_root / f"{name}{ext}"
            if src.exists():
                shutil.copy2(src, wf_dir / f"{name}{ext}")
            # handle nested path in name (shouldn't for exclusive)


def update_wireframe_paths_local(text: str, names: list[str]) -> str:
    for name in names:
        text = text.replace(
            f"[source](../wireframes/{name}.excalidraw)",
            f"[source](./wireframes/{name}.excalidraw)",
        )
        text = text.replace(
            f"[preview](../wireframes/{name}.svg)",
            f"[preview](./wireframes/{name}.svg)",
        )
    return text


def migrate() -> None:
    moved: list[tuple[str, str]] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        domain = domain_dir.name
        for md in sorted(domain_dir.glob("*.md")):
            if md.name == "README.md":
                continue
            long_slug = md.stem
            short = SHORT_SLUG.get(long_slug)
            if not short:
                print(f"MISSING SHORT_SLUG: {domain}/{long_slug}", file=sys.stderr)
                sys.exit(1)
            dest_dir = domain_dir / short
            dest_dir.mkdir(parents=True, exist_ok=True)
            (dest_dir / "diagrams").mkdir(exist_ok=True)

            text = rewrite_content(md.read_text(encoding="utf-8"), domain, short)
            if short in EXCLUSIVE_WIREFRAMES:
                copy_wireframes(domain_dir, short, EXCLUSIVE_WIREFRAMES[short])
                text = update_wireframe_paths_local(text, EXCLUSIVE_WIREFRAMES[short])

            (dest_dir / "README.md").write_text(text, encoding="utf-8")
            md.unlink()
            moved.append((f"{domain}/{long_slug}.md", f"{domain}/{short}/"))

    print(f"Migrated {len(moved)} use cases into folders")


if __name__ == "__main__":
    migrate()
