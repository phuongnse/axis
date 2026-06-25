---
name: axis-doc-hygiene
description: Keep Axis guidance concise, correct, linked, and stable over time. Use when changing any artifact that instructs humans, agents, bots, or checks how to understand or work with Axis, including docs, status/spec text, guidance configs, policy text, anchors, navigation, or documentation ownership.
---

# Axis Doc Hygiene

## Goal

Change guidance without growing context load or drifting from the source of truth.

## Workflow

1. Classify by responsibility, not filename.
   - Product/spec/status surface: keep current behavior, gaps, and AC ownership precise.
   - Process/routing surface: name the decision rule and hand off workflow to the owning skill.
   - Policy/enforcement surface: separate rule owner, deterministic check, and review-only guidance.
   - Tool/bot/agent guidance surface: cite current owners and avoid stale source names.
   - Visual or script-command surface: use the matching visual/script skill.

2. Preserve ownership.
   - One fact has one owner; link instead of restating.
   - Prefer durable categories over inventories of current files, commands, statuses, or tools.
   - Generalize review or incident learning into decision criteria; do not publish symptom-to-remedy recipes.
   - Put repeatable workflow in skills, not prose docs.
   - Treat guidance configs and skill text as maintained docs, not throwaway config.
   - Keep enforcement status in the enforcement ledger, not scattered prose.

3. Edit tightly.
   - Lead with the rule.
   - Delete historical logs, filler, duplicated command matrices, and process prose.
   - Preserve headings/anchors that are linked from other files.
   - Replace stale source names, renamed skills, and removed anchors immediately.
   - Replace low-level lists with high-level strict rules unless the file is the owner inventory.
   - Add new guidance only for a separate responsibility that justifies another surface.

4. Check the right things.
   - Use `$axis-script-scope` to choose the smallest proof for the changed surface.
   - Validate skill text when skills changed.
   - Validate size, links, shape, or drift when that responsibility changed.
   - Use `$axis-ready-review` before review.

## Output

Report changed docs, ownership decisions, generalization check, checks run, and any deferred cleanup with an owner.
