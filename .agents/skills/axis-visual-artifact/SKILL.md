---
name: axis-visual-artifact
description: Update Axis visual artifacts safely. Use when changing design-source links, previews, Mermaid diagrams, diagram theme configuration, or visual artifacts referenced from use-case docs.
---

# Axis Visual Artifact

## Goal

Keep Axis visual docs linked, source-first, regenerated where needed, and visually checked instead of letting design evidence drift from the spec.

## Workflow

1. Identify the artifact type.
   - Design source: editable artifact link in a use-case `## Design Sources` row.
   - Preview: committed image/export or `N/A` while no stable preview exists.
   - Mermaid diagram: Markdown diagram using the shared theme.
   - Visual index: tables in the owning use-case or domain README.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/playbooks/design-source.md`
   - `docs/playbooks/visual-artifact-checklist.md`
   - `docs/playbooks/mermaid.md` when Mermaid changes
   - The owning use-case file when a screen or flow changes

3. Edit the source of truth.
   - Edit the editable design source for new/updated screens, not only the committed preview.
   - Edit Mermaid source in Markdown, not rendered output.
   - Keep use-case-specific previews under `docs/use-cases/{domain}/{use-case}/`.

4. Use design agents conservatively.
   - Start read-only: list sources, inspect layer or file structure, and export the current target before changing it.
   - Keep writes small: one board, screen, component set, or naming cleanup per operation.
   - Keep the active tool focused on the exact file, page, board, or artifact being changed.
   - Use editable native objects for the selected source format. Do not use imported SVG/bitmap groups as the source frame.
   - Use stable file, page, board, and frame names that match the owning design-source row.
   - After each write, inspect layers and export the touched board/frame when supported; verify it is visible, editable, unclipped, and free of floating UI chrome.
   - Never paste local connection URLs, transport URLs, keys, tokens, or personal export links into repo docs.

5. Preserve source/preview integrity.
   - A `Source` cell links to an editable design source, never to a preview/export image.
   - A `Preview` cell can be `N/A`, but a non-`N/A` preview must have an editable source in the same row.
   - Source links point to editable design artifacts, not local transport URLs.

6. Refresh derived files.
   - Design sources: export/commit previews only when the use-case `## Design Sources` table has a preview row or review needs a stable snapshot.
   - Mermaid theme changes: run `python scripts/axis.py docs sync-mermaid-theme`.

7. Check links and visual quality.
   - Run the visual artifact checklist before commit.
   - Verify docs tables point to the right source and preview files.
   - Run doc link/navigation checks when Markdown references change.

8. Verify.
   - Use the narrow visual/docs check that matches the edit.
   - Markdown links: `python scripts/axis.py check markdown-links` when links or anchors changed.
   - Use `$axis-ready-review` before review; it owns full ready-review verification.

## Output

Report design sources edited, previews refreshed or intentionally `N/A`, checklist result, and doc checks.
