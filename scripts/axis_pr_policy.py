"""Shared branch, commit, and pull-request publication policy."""

from __future__ import annotations

import re


CONVENTIONAL_SUBJECT_MAX_LENGTH = 72
CONVENTIONAL_SUBJECT_RE = re.compile(
    r"^[a-z]+(?:\([a-z0-9-]+\))?!?: \S(?:.*\S)?$"
)
BRANCH_RE = re.compile(
    r"^(?:feat|fix|docs|refactor|test|chore|build|ci|perf)/"
    r"[a-z0-9]+(?:-[a-z0-9]+)*$"
)
RENOVATE_BRANCH_RE = re.compile(
    r"^automation/renovate/[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?$"
)

PR_TITLE_EXAMPLE = "feat(identity): implement standalone user registration"
COMMIT_SUBJECT_EXAMPLE = "fix: reject invalid publication metadata"
BRANCH_EXAMPLE = "feat/short-description"


def validate_conventional_subject(
    subject: str,
    *,
    label: str,
    example: str,
) -> list[str]:
    stripped = subject.strip()
    if not stripped:
        return [f"{label} is empty; use Conventional Commit style, e.g. `{example}`"]

    issues: list[str] = []
    if subject != stripped or CONVENTIONAL_SUBJECT_RE.fullmatch(stripped) is None:
        issues.append(
            f"{label} must use Conventional Commit style: "
            "`type(scope): subject` or `type: subject`, "
            f"e.g. `{example}`"
        )
    if len(stripped) > CONVENTIONAL_SUBJECT_MAX_LENGTH:
        issues.append(
            f"{label} must be at most {CONVENTIONAL_SUBJECT_MAX_LENGTH} characters "
            f"({len(stripped)} found)"
        )
    if stripped.endswith("."):
        issues.append(f"{label} must not end with a period")
    return issues


def validate_pr_title(title: str) -> list[str]:
    return validate_conventional_subject(
        title,
        label="PR title",
        example=PR_TITLE_EXAMPLE,
    )


def validate_commit_subject(subject: str) -> list[str]:
    return validate_conventional_subject(
        subject,
        label="Commit subject",
        example=COMMIT_SUBJECT_EXAMPLE,
    )


def validate_branch(branch: str) -> list[str]:
    branch = branch.strip()
    if not branch:
        return ["PR branch is unavailable; run from a named branch or pass `--branch`"]
    if BRANCH_RE.fullmatch(branch) or RENOVATE_BRANCH_RE.fullmatch(branch):
        return []
    return [
        "PR branch must follow CONTRIBUTING.md: "
        "`{type}/{short-description}` in kebab-case or "
        "`automation/{owner}/{description}`, "
        f"e.g. `{BRANCH_EXAMPLE}`"
    ]
