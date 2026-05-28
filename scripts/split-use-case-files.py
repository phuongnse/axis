#!/usr/bin/env python3
"""Split grouped use-case markdown files into one file per `### Use case —` section."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
USE_CASES = ROOT / "docs" / "use-cases"
SKIP = {"README.md", "_template-use-case.md"}
USE_CASE_HEADING = re.compile(r"^### Use case — (.+)$", re.MULTILINE)
STORY_RE = re.compile(
    r"\*\*As an?\*\*\s+([^,]+),\s+\*\*I want(?:\s+to)?\*\*\s+(.+?)\s+\*\*so that\*\*\s+(.+?)\.\s*$",
    re.MULTILINE | re.DOTALL,
)


def slugify(title: str) -> str:
    slug = title.lower().strip()
    slug = re.sub(r"[^a-z0-9]+", "-", slug)
    slug = re.sub(r"-+", "-", slug).strip("-")
    return slug or "use-case"


def extract_block(text: str, heading: str) -> str:
    pattern = re.compile(
        rf"^{re.escape(heading)}\n(.*?)(?=\n## |\Z)",
        re.MULTILINE | re.DOTALL,
    )
    match = pattern.search(text)
    return match.group(1).strip() if match else ""


def parse_story(body: str) -> tuple[str, str, str, str]:
    match = STORY_RE.search(body)
    if not match:
        return (
            "_(One sentence about user value.)_",
            "- _(Actor)_",
            "- _(What starts the use case.)_",
            body,
        )
    actor, want, goal = match.groups()
    purpose = f"{want.strip()} so that {goal.strip()}."
    return (
        purpose,
        f"- {actor.strip()}",
        f"- User initiates: {want.strip()}",
        body,
    )


def build_use_case_file(
    domain: str,
    title: str,
    body: str,
    wireframes: str,
    diagrams: str,
    description: str,
) -> str:
    purpose, actor, trigger, ac_body = parse_story(body)

    # Strip user-story line from AC block if present
    ac_body = STORY_RE.sub("", ac_body, count=1).strip()
    if "**Acceptance Criteria:**" in ac_body:
        ac_section = ac_body
    elif "## Acceptance Criteria" not in ac_body:
        ac_section = f"## Acceptance Criteria\n\n{ac_body}"
    else:
        ac_section = ac_body

    if not ac_section.lstrip().startswith("##"):
        ac_section = f"## Acceptance Criteria\n\n{ac_section}"

    desc_block = ""
    if description:
        desc_block = f"\n## Context\n\n{description}\n"

    return f"""# Use case — {title}

> **Navigation**: [← {domain.replace("-", " ").title()}](./README.md)

## Purpose

{purpose}

## Primary actor

{actor}

## Trigger

{trigger}

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.
{desc_block}
{ac_section}

## Wireframes

{wireframes}

## Diagrams

{diagrams}
"""


def split_file(path: Path) -> list[Path]:
    text = path.read_text(encoding="utf-8")
    matches = list(USE_CASE_HEADING.finditer(text))
    if len(matches) <= 1:
        return []

    domain_dir = path.parent
    domain = domain_dir.name

    wireframes_block = extract_block(text, "## Wireframes")
    if "| Screen | Excalidraw | Preview |" in wireframes_block:
        wireframes = wireframes_block
    else:
        wireframes = (
            "| Screen | Excalidraw | Preview |\n"
            "|--------|------------|---------|\n"
            "| _(none — add when UI is defined)_ | N/A | N/A |"
        )

    diagrams_block = extract_block(text, "## Diagrams")
    if "| Diagram | Source | Preview |" in diagrams_block:
        diagrams = diagrams_block
    else:
        diagrams = (
            "| Diagram | Source | Preview |\n"
            "|---------|--------|---------|\n"
            "| _(none)_ | N/A | N/A |"
        )

    description = extract_block(text, "## Description")

    created: list[Path] = []
    used_slugs: dict[str, int] = {}

    for index, match in enumerate(matches):
        title = match.group(1).strip()
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        section_body = text[start:end].strip()

        base_slug = slugify(title)
        count = used_slugs.get(base_slug, 0)
        used_slugs[base_slug] = count + 1
        slug = base_slug if count == 0 else f"{base_slug}-{count + 1}"

        out_path = domain_dir / f"{slug}.md"
        content = build_use_case_file(domain, title, section_body, wireframes, diagrams, description)
        out_path.write_text(content, encoding="utf-8")
        created.append(out_path)

    path.unlink()
    return created


def regenerate_domain_readme(domain_dir: Path) -> None:
    readme = domain_dir / "README.md"
    if not readme.exists():
        return

    text = readme.read_text(encoding="utf-8")
    files = sorted(
        p
        for p in domain_dir.glob("*.md")
        if p.name not in SKIP and p.name != "README.md"
    )
    if not files:
        return

    rows: list[str] = []
    for p in files:
        first_line = p.read_text(encoding="utf-8").splitlines()[0]
        title = first_line.removeprefix("# Use case — ").strip()
        purpose_match = re.search(r"^## Purpose\n\n(.+)$", p.read_text(encoding="utf-8"), re.MULTILINE)
        desc = purpose_match.group(1).strip() if purpose_match else ""
        if desc.startswith("_("):
            desc = title
        if len(desc) > 120:
            desc = desc[:117] + "..."
        rows.append(f"| [{title}]({p.name}) | {desc} |")

    table = "| Use case | Summary |\n|---|---|\n" + "\n".join(rows) + "\n"

    if "## Use Cases" in text:
        text = re.sub(
            r"## Use Cases\n\n.*?(?=\n---\n|\n## [^U]|\Z)",
            f"## Use Cases\n\n{table}\n",
            text,
            count=1,
            flags=re.DOTALL,
        )
    else:
        text += f"\n## Use Cases\n\n{table}\n"

    readme.write_text(text, encoding="utf-8")


def update_cross_links(created: list[Path], removed: list[Path]) -> None:
    """Replace links to removed group files with README when no 1:1 mapping."""
    mapping = {p.stem: p.name for p in created}
    for old in removed:
        old_name = old.name
        for path in ROOT.rglob("*"):
            if path.suffix not in {".md", ".cs", ".ts", ".tsx", ".mjs", ".sh", ".py"}:
                continue
            if "node_modules" in path.parts or ".git" in path.parts:
                continue
            try:
                content = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            if old_name not in content:
                continue
            # Leave to manual / README index — only fix obvious same-dir single-target
            new_content = content.replace(f"]({old_name})", f"](./README.md)")
            new_content = new_content.replace(f"({old_name})", f"(./README.md)")
            if new_content != content:
                path.write_text(new_content, encoding="utf-8")


def main() -> int:
    removed: list[Path] = []
    all_created: list[Path] = []
    domains_touched: set[Path] = set()

    for path in sorted(USE_CASES.glob("*/*.md")):
        if path.name in SKIP:
            continue
        created = split_file(path)
        if created:
            removed.append(path)
            all_created.extend(created)
            domains_touched.add(path.parent)
            print(f"split {path.relative_to(ROOT)} -> {len(created)} files")

    for domain_dir in domains_touched:
        regenerate_domain_readme(domain_dir)

    # Fix links in docs only: point old group filenames to domain README
    for old in removed:
        old_rel = old.name
        for doc in (ROOT / "docs").rglob("*"):
            if doc.suffix != ".md":
                continue
            text = doc.read_text(encoding="utf-8")
            if old_rel not in text:
                continue
            # Prefer linking to README; agents use domain index
            new_text = text.replace(f"]({old_rel})", "](./README.md)")
            new_text = new_text.replace(f"](../{old.parent.name}/{old_rel})", f"](../{old.parent.name}/README.md)")
            if new_text != text:
                doc.write_text(new_text, encoding="utf-8")

    print(f"Created {len(all_created)} use-case files; removed {len(removed)} group files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
