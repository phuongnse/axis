---
name: axis-visual-artifact
description: Update Axis visual artifacts safely. Use when changing Mermaid diagrams or committed visual artifacts referenced from use-case docs.
---

# Axis Visual Artifact

## Goal

Keep Axis visual docs linked, regenerated where needed, and visually checked instead of letting visual evidence drift from the spec.

## Hard gates

Follow [reference.md](../reference.md).
- Do not paste local transport URLs, keys, or tokens into repo docs.

## Inputs

- Owning use-case and artifact type: Mermaid or committed visual.
- Editable Markdown diagram source or committed visual source to change.
- Expected link/check evidence for the touched artifact.

## Workflow

1. Identify the artifact type.
   - Mermaid diagram: Markdown diagram using default Mermaid rendering.
   - Visual index: tables in the owning use-case or domain README.

2. Read the owning rules.
   - [AGENTS.md](../../../AGENTS.md)
   - [docs/playbooks/docs-style.md](../../../docs/playbooks/docs-style.md)
   - The owning use-case file when a screen or flow changes

3. Edit the source of truth.
   - Edit Mermaid source in Markdown, not rendered output.
   - Use concise labels that match the flow vocabulary, not implementation names by default.
   - Keep use-case-specific visual files under `docs/use-cases/{domain}/{use-case}/`.

4. Use design agents conservatively.
   - Start read-only: list sources, inspect layer or file structure, and export the current target before changing it.
   - Keep writes small: one board, screen, component set, or naming cleanup per operation.
   - Keep the active tool focused on the exact file, page, board, or artifact being changed.
   - Use editable native objects for the selected source format. Do not use imported SVG/bitmap groups as the source frame.
   - Use stable file, page, board, and frame names that match the owning use-case vocabulary.
   - After each write, inspect layers and export the touched board/frame when supported; verify it is visible, editable, unclipped, and free of floating UI chrome.
   - Never paste local connection URLs, transport URLs, keys, tokens, or personal export links into repo docs.

5. Preserve visual integrity.
   - Check meaning, connectors, labels, spacing, clipping, and handoff boundaries before reporting visual work done.

6. Refresh derived files.
   - Mermaid diagrams stay as plain Markdown source with default rendering.

7. Check links and visual quality.
   - Preview Mermaid or committed visual assets in GitHub/IDE when practical.
   - Check that diagram labels, connectors, and handoff boundaries match the owning spec.
   - Run doc link/navigation checks when Markdown references change.

8. Verify.
   - Use the narrow visual/docs check that matches the edit.
   - Markdown links: `python scripts/axis.py check markdown-links` when links or anchors changed.
   - Use `$axis-ready-review` before review; it owns full ready-review verification.

## Output

Report diagrams or committed visuals edited, visual-quality result, and doc checks.
