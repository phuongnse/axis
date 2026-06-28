# Axis Repo Skills

Portable agent workflows for this repository. Standard layout: one directory per skill with a required `SKILL.md` (YAML frontmatter + markdown body). Optional extras: `reference.md`, `examples.md`, `scripts/`.

Project skills live under [`.cursor/skills/`](.) per the Agent Skills format used by Cursor and other agents that read repo files.

## How agents use skills

1. Match the task to a skill in the table below (or use the skill `description` in frontmatter).
2. **Read the full `SKILL.md` file before editing** implementation files.
3. Follow chained skills the same way: read their `SKILL.md` paths, not just the alias name.
4. Run repo commands through `python scripts/axis.py ...` as written in the skill.

`$axis-*` names in docs are **aliases** for `.cursor/skills/<name>/SKILL.md`.

Validate skill changes: `python scripts/axis.py check repo-skills`

## Task → skill

| Task | Read first |
|---|---|
| Explore / audit / read-only | [axis-script-scope/SKILL.md](./axis-script-scope/SKILL.md) |
| Pre-code dossier / non-trivial change | [axis-design-gate/SKILL.md](./axis-design-gate/SKILL.md) |
| Draft or complete use-case spec | [axis-use-case-spec/SKILL.md](./axis-use-case-spec/SKILL.md) |
| Implement documented use case | [axis-use-case-implementation/SKILL.md](./axis-use-case-implementation/SKILL.md) |
| API / OpenAPI / generated types | [axis-api-contract/SKILL.md](./axis-api-contract/SKILL.md) |
| Frontend feature slice | [axis-frontend-feature/SKILL.md](./axis-frontend-feature/SKILL.md) |
| Visual / design source / Mermaid | [axis-visual-artifact/SKILL.md](./axis-visual-artifact/SKILL.md) |
| Docs / guidance / status edits | [axis-doc-hygiene/SKILL.md](./axis-doc-hygiene/SKILL.md) |
| Repo tooling / compose / local-dev / scripts | [axis-script-scope/SKILL.md](./axis-script-scope/SKILL.md) |
| Branch ready for review | [axis-ready-review/SKILL.md](./axis-ready-review/SKILL.md) |
| Open / update / mark-ready PR | [axis-pull-request/SKILL.md](./axis-pull-request/SKILL.md) |
| Review feedback follow-up | [axis-review-feedback/SKILL.md](./axis-review-feedback/SKILL.md) |

## Typical flow

```text
spec gap     → axis-use-case-spec
non-trivial  → axis-design-gate → implement skill → axis-script-scope (checks)
before review→ axis-ready-review
PR action    → axis-pull-request → axis-review-feedback (if needed)
```
