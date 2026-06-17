---
name: axis-visual-artifact
description: Update Axis wireframes and diagrams safely. Use when changing docs wireframes, use-case Excalidraw files, generated SVG previews, Mermaid diagrams, diagram theme configuration, or visual artifacts referenced from use-case docs.
---

# Axis Visual Artifact

## Goal

Keep Axis visual docs source-controlled, regenerated, linked, and visually checked instead of editing previews by hand.

## Workflow

1. Identify the artifact type.
   - Wireframe source: `.excalidraw`.
   - Generated preview: `.svg` next to the source.
   - Mermaid diagram: Markdown diagram using the shared theme.
   - Use-case visual index: tables in the owning use-case or domain README.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/playbooks/visual-artifact-checklist.md`
   - `docs/playbooks/wireframes.md`
   - `docs/wireframes/README.md`
   - `docs/playbooks/mermaid.md` when Mermaid changes
   - The owning use-case file when a screen or flow changes

3. Edit the source of truth.
   - Edit `.excalidraw` for wireframes, not only the generated `.svg`.
   - Edit Mermaid source in Markdown, not rendered output.
   - Keep shared app shell assets under `docs/wireframes/`.
   - Keep use-case-specific assets under `docs/use-cases/{domain}/{use-case}/`.

4. Regenerate derived files.
   - Wireframes: `python scripts/axis.py generate wireframes`.
   - Mermaid theme changes: `python docs/scripts/sync-mermaid-theme.py`.
   - Include regenerated `.svg` files in the same change when `.excalidraw` changes.

5. Check links and visual quality.
   - Run the visual artifact checklist before commit.
   - Verify docs tables point to the right source and preview files.
   - Run doc link/navigation checks when Markdown references change.

6. Verify.
   - Docs/scripts/policy changes: `python scripts/axis.py check policy-tests`.
   - Docs drift: `python scripts/axis.py check doc-drift`.
   - Ready review: `$axis-ready-review`.

## Output

Report source files edited, generated files refreshed, checklist result, and doc checks.
