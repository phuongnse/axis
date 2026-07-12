---
name: axis-doc-hygiene
description: Keep durable Axis documentation, guidance, diagrams, links, status text, and ownership concise and current. Use when those artifacts change; this skill governs documentation quality, not the underlying domain decision.
---

# Axis Doc Hygiene

## Goal

Keep one owner per fact and prevent guidance from accumulating duplicate workflow, incident history, or stale names.

## Hard gates

Follow [reference.md](../reference.md).
- Preserve the decision supplied by the domain owner; do not create product, stack, or architecture policy here.
- Use [docs/playbooks/design-gate.md § Dossier](../../../docs/playbooks/design-gate.md#dossier) for retired guidance surfaces.
- Keep enforcement status only in [docs/ENFORCEMENT.md](../../../docs/ENFORCEMENT.md).

## Inputs

- Changed guidance or visual artifact and its current owner.
- Domain decision supplied by the entry workflow.
- Retired identifiers, when applicable.

## Workflow

1. Classify the responsibility: product/spec/status, process/routing, policy/enforcement, stack, navigation/link, or visual source.
2. Locate the single owner. Edit it once and replace other copies with inline owner links.
3. Apply [docs/playbooks/docs-style.md](../../../docs/playbooks/docs-style.md): lead with the rule, keep current contracts, remove filler/history, and preserve linked anchors.
4. For Mermaid or committed visuals, edit the owning source, use product vocabulary, verify labels/connectors/clipping, and keep local URLs, keys, tokens, and personal exports out of the repo.
5. Apply [reference.md § Improvement loop](../reference.md#improvement-loop) when feedback exposes a reusable gap; do not publish symptom-to-remedy recipes.
6. Run the narrow docs/skills/link check directly, or this workflow **Delegates** command selection to `$axis-script-scope` and resumes with its evidence.

## Output

Report owner changed, duplicates pruned, visual result when applicable, retirement sweep, checks, and unresolved ownership decisions.
