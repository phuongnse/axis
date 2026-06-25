# Design Source Playbook

> **Navigation**: [<- docs/README.md](../README.md) . [<- visual checklist](./visual-artifact-checklist.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-visual-artifact` for design-source links, previews, Mermaid, and visual docs.

## Decisions

Design sources are editable artifacts. Previews are derived evidence. Runtime Axis must not depend on design-tool infrastructure.

## Source Organization

Use-case-specific sources/previews live with the owning use case. Shared design assets live under their owning design-source package.

## Open Design Package

The shared Open Design package is the source for reusable visual language. Do not commit secrets, local transport URLs, or personal export links.

## Design-First Workflow

For screen changes: update editable source, update owning use-case Design Sources row, refresh preview when needed, then implement UI against the spec.

## Agent Workflow

Start read-only, make small focused edits, inspect/export the touched frame, and keep source/preview links paired.

## Verification

Run the visual artifact checklist when visuals change, link checks when Markdown references change, and `$axis-ready-review` before review.
