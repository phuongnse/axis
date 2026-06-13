#!/usr/bin/env python3
"""Validate flow-first use-case docs structure.

Usage:
  python3 scripts/check-use-case-docs.py --check
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
USE_CASES = ROOT / "docs" / "use-cases"
SKIP_DIRS = set()

REQUIRED_HEADINGS = ["## Wireframes"]
IMPLEMENTATION_STATUS_HEADING = "> **Implementation status**"
IMPLEMENTATION_STATUS_REQUIRED_MARKERS = (
    ("> **Gaps vs spec:**", "gaps vs spec"),
    ("> **Deferred follow-ups:**", "deferred follow-ups"),
    ("> **Decisions:**", "decisions"),
)
LEGACY_DEFERRED_STATUS_RE = re.compile(r"^> \*\*Deferred(?: \([^)]*\))?:\*\*", re.MULTILINE)
PENDING_STATUS_CODEPOINTS = {0x23F3, 0x26A0}
GENERIC_STATUS_PROSE = (
    "Open work remains in layers marked pending above.",
    "Complete the open items listed in Gaps vs spec before marking this use case complete.",
)

LEGACY_DIAGRAM_TABLE_HEADER = "| Diagram | Source | Preview |"

# Placeholder markers from USE_CASE_TEMPLATE.md that must be replaced before a
# use case can be considered written. Listed individually so the validator can
# point at the missing field.
PURPOSE_PLACEHOLDERS = (
    "_(One sentence about user value.)_",
    "<One sentence about user value.>",
)
ACTOR_PLACEHOLDERS = (
    "- _(Actor)_",
    "- <actor>",
)
TRIGGER_PLACEHOLDERS = (
    "- _(What starts the use case.)_",
    "- <what starts the use case>",
)
# The 3-line stock Main flow shipped by the migration. If a use case still has
# this verbatim, no real flow has been written yet.
TEMPLATE_MAIN_FLOW = (
    "1. Actor satisfies the trigger.\n"
    "2. System performs the happy-path steps in Acceptance Criteria.\n"
    "3. Actor receives the expected outcome."
)


def implementation_status_callout(text: str) -> str:
    lines = text.splitlines()
    for idx, line in enumerate(lines):
        if line != IMPLEMENTATION_STATUS_HEADING:
            continue
        block: list[str] = []
        for block_line in lines[idx:]:
            if not block_line.startswith(">"):
                break
            block.append(block_line)
        return "\n".join(block)
    return ""


def callout_has_pending_layer(callout: str) -> bool:
    return any(
        line.startswith("> |") and any(ord(ch) in PENDING_STATUS_CODEPOINTS for ch in line)
        for line in callout.splitlines()
    )


def callout_section_content(callout: str, marker: str) -> list[str]:
    lines = callout.splitlines()
    for idx, line in enumerate(lines):
        if not line.startswith(marker):
            continue
        content: list[str] = []
        inline = line.removeprefix(marker).strip()
        if inline:
            content.append(inline)
        for next_line in lines[idx + 1 :]:
            if next_line.startswith("> **") and ":**" in next_line:
                break
            if next_line.startswith("> |"):
                break
            stripped = next_line.removeprefix(">").strip()
            if stripped:
                content.append(stripped)
        return content
    return []


def iter_use_case_files() -> list[Path]:
    files: list[Path] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        for readme in sorted(domain_dir.glob("*/README.md")):
            if readme.parent.name in SKIP_DIRS:
                continue
            files.append(readme)
    return files


def check_file(path: Path, *, strict_status: bool = True) -> list[str]:
    issues: list[str] = []
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT)

    if re.search(r"^#\s*F\d+\s*[—-]\s*", text, re.MULTILINE):
        issues.append(f"{rel}: legacy Fxx title found")

    for heading in REQUIRED_HEADINGS:
        if heading not in text:
            issues.append(f"{rel}: missing required heading `{heading}`")

    has_purpose = "## Purpose" in text
    has_actor = "## Primary actor" in text
    has_trigger = "## Trigger" in text
    has_main_flow = "## Main flow" in text
    has_alt_flow = "## Alternate / error flows" in text
    has_ac = "## Acceptance Criteria" in text

    if not has_purpose:
        issues.append(f"{rel}: missing purpose section")
    if not has_actor:
        issues.append(f"{rel}: missing actor section")
    if not has_trigger:
        issues.append(f"{rel}: missing trigger section")
    if not has_main_flow:
        issues.append(f"{rel}: missing main flow section")
    if not has_alt_flow:
        issues.append(f"{rel}: missing alternate/error flows section")
    if not has_ac:
        issues.append(f"{rel}: missing acceptance criteria section")

    wireframes_section = ""
    if "## Wireframes" in text:
        wireframes_section = text.split("## Wireframes", 1)[1].split("\n## ", 1)[0]
    if not (
        "| Screen |" in wireframes_section
        and "| Excalidraw |" in wireframes_section
        and "| Preview |" in wireframes_section
    ):
        issues.append(f"{rel}: missing wireframes table (need Screen, Excalidraw, Preview columns)")

    if LEGACY_DIAGRAM_TABLE_HEADER in text:
        issues.append(
            f"{rel}: legacy Excalidraw diagrams table — use Mermaid under ## Diagrams "
            "(docs-style) or omit ## Diagrams when there is no local diagram",
        )
    elif "## Diagrams" in text:
        diagrams_section = text.split("## Diagrams", 1)[1].split("\n## ", 1)[0]
        if "```mermaid" not in diagrams_section:
            issues.append(
                f"{rel}: ## Diagrams must contain at least one ```mermaid block "
                "(or omit ## Diagrams when there is no local diagram)",
            )

    callout = implementation_status_callout(text)
    if not callout:
        issues.append(f"{rel}: missing implementation status callout")
    elif strict_status:
        for marker, label in IMPLEMENTATION_STATUS_REQUIRED_MARKERS:
            if marker not in callout:
                issues.append(f"{rel}: missing implementation status {label} section")
        if LEGACY_DEFERRED_STATUS_RE.search(callout):
            issues.append(f"{rel}: legacy Deferred status heading found - use `Deferred follow-ups`")
        for marker, label in IMPLEMENTATION_STATUS_REQUIRED_MARKERS:
            content = callout_section_content(callout, marker)
            if not content:
                issues.append(f"{rel}: implementation status {label} section is empty")
            if any(generic in line for generic in GENERIC_STATUS_PROSE for line in content):
                issues.append(f"{rel}: implementation status {label} uses generic placeholder prose")
        if callout_has_pending_layer(callout):
            if any(
                line in {
                    "> **Gaps vs spec:** none",
                    "> **Gaps vs spec:** none.",
                    "> **Gaps vs spec:** none for the current documented scope.",
                }
                for line in callout.splitlines()
            ):
                issues.append(f"{rel}: pending layer cannot use `Gaps vs spec: none`")

    for placeholder in PURPOSE_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Purpose still has placeholder `{placeholder}`")
    for placeholder in ACTOR_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Primary actor still has placeholder `{placeholder.strip()}`")
    for placeholder in TRIGGER_PLACEHOLDERS:
        if placeholder in text:
            issues.append(f"{rel}: Trigger still has placeholder `{placeholder.strip()}`")

    return issues


def count_template_main_flow(files: list[Path]) -> int:
    return sum(1 for p in files if TEMPLATE_MAIN_FLOW in p.read_text(encoding="utf-8"))


def diff_range_against_base() -> str:
    """Return the PR-style git range; fail closed without diff context."""
    base = os.environ.get("BASE_BRANCH", "main")
    candidates = [f"origin/{base}", base]
    range_spec: str | None = None

    for candidate in candidates:
        result = subprocess.run(
            ["git", "rev-parse", "--verify", candidate],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if result.returncode == 0:
            range_spec = f"{candidate}...HEAD"
            break

    if range_spec is None:
        result = subprocess.run(
            ["git", "rev-parse", "--verify", "HEAD~1"],
            cwd=ROOT,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        if result.returncode == 0:
            range_spec = "HEAD~1...HEAD"

    if range_spec is None:
        raise RuntimeError(
            "check-use-case-docs: failed to find a git diff base "
            f"(tried origin/{base}, {base}, and HEAD~1)"
        )

    return range_spec


def changed_paths_against_base() -> list[Path]:
    """Return changed paths for PR-style ratchets; fail closed without diff context."""
    range_spec = diff_range_against_base()
    result = subprocess.run(
        ["git", "diff", "--name-only", range_spec],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(f"check-use-case-docs: failed to diff {range_spec}")

    paths: list[Path] = []
    for line in result.stdout.splitlines():
        if line.strip():
            paths.append((ROOT / line.strip()).resolve())
    return paths


def strip_implementation_status_callouts(text: str) -> str:
    """Remove status callout blocks so ratchets can distinguish status-only edits."""
    lines = text.splitlines()
    out: list[str] = []
    idx = 0
    while idx < len(lines):
        if lines[idx] == IMPLEMENTATION_STATUS_HEADING:
            idx += 1
            while idx < len(lines) and lines[idx].startswith(">"):
                idx += 1
            continue
        out.append(lines[idx])
        idx += 1
    return "\n".join(out).strip()


def changed_use_case_content_outside_status(path: Path, range_spec: str) -> bool:
    base_ref = range_spec.split("...", 1)[0]
    relative = path.relative_to(ROOT).as_posix()
    result = subprocess.run(
        ["git", "show", f"{base_ref}:{relative}"],
        cwd=ROOT,
        encoding="utf-8",
        errors="replace",
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    previous = result.stdout if result.returncode == 0 else ""
    current = path.read_text(encoding="utf-8")
    return strip_implementation_status_callouts(previous) != strip_implementation_status_callouts(current)


def check_changed_stock_main_flow(files: list[Path]) -> list[str]:
    """Ratchet: existing stock flows are debt; content edits must replace them."""
    range_spec = diff_range_against_base()
    changed = set(changed_paths_against_base())
    if not changed:
        return []

    issues: list[str] = []
    for path in files:
        resolved = path.resolve()
        if resolved not in changed:
            continue
        if TEMPLATE_MAIN_FLOW not in path.read_text(encoding="utf-8"):
            continue
        if not changed_use_case_content_outside_status(path, range_spec):
            continue
        rel = path.relative_to(ROOT)
        issues.append(
            f"{rel}: changed use-case content still has the stock Main flow — replace it with the real user/system flow"
        )
    return issues


# Row in a domain README that references `./README.md` from inside that same
# README is a self-link — it resolves to the current page and conveys
# nothing.
SELF_LINK_RE = re.compile(r"\]\(\./README\.md(?:#[^)]*)?\)")

# Suspected truncation in a `| ... | summary |` row: the summary cell ends
# without sentence-terminating punctuation and is roughly the length of the
# old hard 100-char cut. Tolerates legit short cells (≤80 chars), legit
# punctuation, and lines that are not table rows.
TABLE_ROW_RE = re.compile(r"^\|[^|]+\|\s*([^|]+?)\s*\|\s*$", re.MULTILINE)
TRUNCATION_TERMINAL = (".", "?", "!", "…", ":")


def check_domain_readme(path: Path) -> list[str]:
    """Domain README-only checks (self-links + truncated table rows)."""
    issues: list[str] = []
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT)

    for match in SELF_LINK_RE.finditer(text):
        line_no = text.count("\n", 0, match.start()) + 1
        issues.append(
            f"{rel}:{line_no}: self-link to ./README.md — replace with a real use-case slug or drop the link"
        )

    in_use_cases_section = False
    for idx, line in enumerate(text.splitlines(), start=1):
        stripped = line.strip()
        if stripped.startswith("## Use Cases") or stripped.startswith("### "):
            in_use_cases_section = stripped == "## Use Cases" or in_use_cases_section
        if stripped.startswith("## ") and not stripped.startswith("## Use Cases"):
            in_use_cases_section = False
        if not in_use_cases_section:
            continue
        match = TABLE_ROW_RE.match(line)
        if not match:
            continue
        summary = match.group(1).strip()
        if len(summary) < 80:
            continue
        if summary.endswith(TRUNCATION_TERMINAL):
            continue
        issues.append(
            f"{rel}:{idx}: suspected truncated summary row (no terminal punctuation, {len(summary)} chars): {summary[-40:]!r}"
        )

    return issues


def iter_domain_readmes() -> list[Path]:
    readmes: list[Path] = []
    for domain_dir in sorted(USE_CASES.iterdir()):
        if not domain_dir.is_dir() or domain_dir.name.startswith("_"):
            continue
        readme = domain_dir / "README.md"
        if readme.is_file():
            readmes.append(readme)
    return readmes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="validate and exit non-zero on issues")
    _ = parser.parse_args()

    files = iter_use_case_files()
    issues: list[str] = []
    changed_paths: set[Path] = set()
    try:
        changed_paths = set(changed_paths_against_base())
    except RuntimeError as exc:
        issues.append(str(exc))
    for path in files:
        issues.extend(check_file(path, strict_status=path.resolve() in changed_paths))
    try:
        issues.extend(check_changed_stock_main_flow(files))
    except RuntimeError as exc:
        issues.append(str(exc))
    for readme in iter_domain_readmes():
        issues.extend(check_domain_readme(readme))

    if issues:
        print("Use-case docs validation failed:", file=sys.stderr)
        for issue in issues:
            print(f"  - {issue}", file=sys.stderr)
        return 1

    # Soft signal: number of use cases that still ship the stock Main flow.
    # Tracked here so the debt is visible without blocking the build.
    stub_count = count_template_main_flow(files)
    if stub_count:
        print(
            f"check-use-case-docs: OK ({stub_count} of {len(files)} files still have the stock Main flow)"
        )
    else:
        print("check-use-case-docs: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
