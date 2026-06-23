---
name: axis-visual-artifact
description: Update Axis wireframes and diagrams safely. Use when changing docs wireframes, Penpot design-source links or previews, legacy Excalidraw assets, Mermaid diagrams, diagram theme configuration, or visual artifacts referenced from use-case docs.
---

# Axis Visual Artifact

## Goal

Keep Axis visual docs linked, source-first, regenerated where needed, and visually checked instead of letting design evidence drift from the spec.

## Workflow

1. Identify the artifact type.
   - Design source: Penpot file/page/frame link in a use-case `## Wireframes` row.
   - Preview: committed image/export or `N/A` while no stable preview exists.
   - Legacy wireframe source: `.excalidraw` with generated `.svg`.
   - Mermaid diagram: Markdown diagram using the shared theme.
   - Visual index: tables in the owning use-case or domain README.

2. Read the owning rules.
   - `AGENTS.md`
   - `docs/playbooks/design-source.md`
   - `docs/playbooks/visual-artifact-checklist.md`
   - `docs/playbooks/wireframes.md`
   - `docs/wireframes/README.md`
   - `docs/playbooks/mermaid.md` when Mermaid changes
   - The owning use-case file when a screen or flow changes

3. Edit the source of truth.
   - Edit the Penpot design source for new/updated wireframes, not only the committed preview.
   - Edit legacy `.excalidraw` only when maintaining an existing legacy asset.
   - Edit Mermaid source in Markdown, not rendered output.
   - Keep shared app shell references under the shared Penpot file or `docs/wireframes/` legacy assets.
   - Keep use-case-specific previews under `docs/use-cases/{domain}/{use-case}/`.

4. Refresh derived files.
   - Penpot: export/commit previews only when the use-case table has a preview row or review needs a stable snapshot.
   - Legacy Excalidraw: run `python scripts/axis.py generate wireframes` and include regenerated `.svg`.
   - Mermaid theme changes: run `python scripts/axis.py docs sync-mermaid-theme`.

5. Check links and visual quality.
   - Run the visual artifact checklist before commit.
   - Verify docs tables point to the right source and preview files.
   - Run doc link/navigation checks when Markdown references change.

6. Verify.
   - Docs/scripts/policy changes: `python scripts/axis.py check policy-tests`.
   - Docs drift: `python scripts/axis.py check doc-drift`.
   - Markdown links: `python scripts/axis.py check markdown-links` when links or anchors changed.
   - Ready review: `$axis-ready-review`.

## Output

Report design sources edited, previews refreshed or intentionally `N/A`, checklist result, and doc checks.
