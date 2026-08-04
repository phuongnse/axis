#!/usr/bin/env python3
"""Validate pull request title and body structure.

Usage:
  PR_TITLE="feat(scope): subject" PR_BODY="..." python scripts/check-pr.py
  python scripts/check-pr.py --title "feat(scope): subject" --body-file pr-body.md
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)
HEADING_RE = re.compile(r"^##\s+(.+?)\s*$", re.MULTILINE)
CHECKBOX_RE = re.compile(r"^\s*-\s+\[(?P<state>[ xX])\]\s+(?P<label>.+)$", re.MULTILINE)
CHECKLIST_STATUS_RE = re.compile(r"\[status:\s*(?P<status>[a-z-]+)\]\s*$", re.IGNORECASE)
CHECKLIST_REASON_RE = re.compile(r"\[reason:\s*(?P<reason>[^\]]*\S[^\]]*)\]", re.IGNORECASE)
CHECKLIST_STATUSES = {"satisfied", "not-applicable", "pending"}
PR_TITLE_RE = re.compile(r"^[a-z]+(?:\([a-z0-9-]+\))?!?: \S.*$")
BRANCH_RE = re.compile(r"^(?:feat|fix|docs|refactor|test|chore)/[a-z0-9]+(?:-[a-z0-9]+)*$")
RENOVATE_BRANCH_RE = re.compile(r"^renovate/[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?$")

REQUIRED_SECTIONS = (
    "Summary",
    "Linked spec",
    "Requirements & rules followed",
)

PR_TITLE_EXAMPLE = "feat(identity): implement standalone user registration"
BRANCH_EXAMPLE = "feat/short-description"


def strip_comments(text: str) -> str:
    return COMMENT_RE.sub("", text)


def sections(body: str) -> dict[str, str]:
    matches = list(HEADING_RE.finditer(body))
    result: dict[str, str] = {}

    for index, match in enumerate(matches):
        name = match.group(1).strip()
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(body)
        result[name] = body[start:end]

    return result


def section_text(parts: dict[str, str], name: str) -> str:
    return strip_comments(parts.get(name, "")).strip()


def validate_title(title: str) -> list[str]:
    title = title.strip()
    if not title:
        return [f"PR title is empty; use Conventional Commit style, e.g. `{PR_TITLE_EXAMPLE}`"]
    if not PR_TITLE_RE.match(title):
        return [
            "PR title must use Conventional Commit style: "
            "`type(scope): subject` or `type: subject`, "
            f"e.g. `{PR_TITLE_EXAMPLE}`",
        ]
    return []


def validate_branch(branch: str) -> list[str]:
    branch = branch.strip()
    if not branch:
        return ["PR branch is unavailable; run from a named branch or pass `--branch`"]
    if BRANCH_RE.fullmatch(branch) or RENOVATE_BRANCH_RE.fullmatch(branch):
        return []
    return [
        "PR branch must follow CONTRIBUTING.md: "
        "`{type}/{short-description}` in kebab-case, "
        f"e.g. `{BRANCH_EXAMPLE}`"
    ]


def validate_body(body: str) -> list[str]:
    body = body.lstrip("\ufeff")
    issues: list[str] = []
    if not strip_comments(body).strip():
        return ["PR body is empty; use .github/PULL_REQUEST_TEMPLATE.md"]

    parts = sections(body)
    for required in REQUIRED_SECTIONS:
        if required not in parts:
            issues.append(f"Missing section: ## {required}")

    summary = section_text(parts, "Summary")
    if "Summary" in parts and not summary:
        issues.append("Summary must be filled in")

    linked_spec = section_text(parts, "Linked spec")
    if "Linked spec" in parts and not linked_spec:
        issues.append("Linked spec must be filled in")

    requirements = parts.get("Requirements & rules followed", "")
    checkboxes = list(CHECKBOX_RE.finditer(requirements))
    if "Requirements & rules followed" in parts and not checkboxes:
        issues.append("Requirements section must include checklist items from the PR template")

    for match in checkboxes:
        line = match.group(0).strip()
        state = match.group("state")
        status_match = CHECKLIST_STATUS_RE.search(line)
        if status_match is None:
            issues.append(f"Requirement is missing a structured status: {line}")
            continue
        status = status_match.group("status").lower()
        if status not in CHECKLIST_STATUSES:
            issues.append(f"Requirement has invalid status `{status}`: {line}")
            continue
        checked = state.lower() == "x"
        if status == "pending" and checked:
            issues.append(f"Pending requirement must be unchecked: {line}")
        elif status != "pending" and not checked:
            issues.append(f"Resolved requirement must be checked: {line}")
        if status == "not-applicable" and CHECKLIST_REASON_RE.search(line) is None:
            issues.append(f"Not-applicable requirement must include `[reason: ...]`: {line}")

    return issues


def validate(title: str, body: str, branch: str | None = None) -> list[str]:
    branch_issues = validate_branch(branch) if branch is not None else []
    return [*validate_title(title), *validate_body(body), *branch_issues]


def resolve_branch(explicit: str | None) -> str:
    if explicit is not None:
        return explicit
    if head_ref := os.environ.get("PR_HEAD_REF", "").strip():
        return head_ref
    try:
        result = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError:
        return ""
    return result.stdout.strip() if result.returncode == 0 else ""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--title", help="PR title. Defaults to PR_TITLE env var")
    parser.add_argument("--body-file", type=Path, help="Read PR body from a file")
    parser.add_argument("--branch", help="PR head branch. Defaults to PR_HEAD_REF or current branch")
    args = parser.parse_args()

    title = args.title if args.title is not None else os.environ.get("PR_TITLE", "")
    if args.body_file:
        body = args.body_file.read_text(encoding="utf-8")
    else:
        body = os.environ.get("PR_BODY", "")

    issues = validate(title, body, resolve_branch(args.branch))
    if issues:
        print("check-pr FAIL:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    print("check-pr: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
