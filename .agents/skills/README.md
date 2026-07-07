# Axis Repo Skills

Portable agent workflows for this repository. Standard layout: one directory per skill with a required `SKILL.md` (YAML frontmatter + markdown body). Optional extras: `reference.md`, `examples.md`, `scripts/`.

Project skills live under [`.agents/skills/`](.) as the canonical, tool-neutral repo workflow path.

Shared workflow contract: [reference.md](./reference.md) — sequential steps, stop/defer/skip rules, and the PR publication gate.

## How agents use skills

1. Match the task to a skill in the table below (or use the skill `description` in frontmatter).
2. **Read [reference.md](./reference.md) and the full `SKILL.md` file before editing** implementation files.
3. Execute numbered workflow steps **in order**; each skill's `## Hard gates` section adds skill-specific stop rules.
4. Follow chained skills the same way: read their `SKILL.md` paths, not just the alias name.
5. Run repo commands through `python scripts/axis.py ...` as written in the skill.

`$axis-*` names in docs are **aliases** for `.agents/skills/<name>/SKILL.md`.

Validate skill changes: `python scripts/axis.py check repo-skills`

## Task → skill

| Task | Read first |
|---|---|
| Explore / audit / read-only | [axis-script-scope/SKILL.md](./axis-script-scope/SKILL.md) |
| Pre-code dossier / non-trivial change | [axis-design-gate/SKILL.md](./axis-design-gate/SKILL.md) |
| New module / DDD / CQRS / event sourcing architecture | [axis-module-architecture/SKILL.md](./axis-module-architecture/SKILL.md) |
| Tactical DDD/CQRS patterns in module code | [axis-module-patterns/SKILL.md](./axis-module-patterns/SKILL.md) |
| Draft or complete use-case spec | [axis-use-case-spec/SKILL.md](./axis-use-case-spec/SKILL.md) |
| Implement documented use case | [axis-use-case-implementation/SKILL.md](./axis-use-case-implementation/SKILL.md) |
| API / OpenAPI / generated types | [axis-api-contract/SKILL.md](./axis-api-contract/SKILL.md) |
| App shell / shared SPA UI infrastructure | [axis-frontend-foundation/SKILL.md](./axis-frontend-foundation/SKILL.md) |
| Frontend feature slice | [axis-frontend-feature/SKILL.md](./axis-frontend-feature/SKILL.md) |
| Mermaid / committed visual artifact | [axis-visual-artifact/SKILL.md](./axis-visual-artifact/SKILL.md) |
| Docs / guidance / status edits | [axis-doc-hygiene/SKILL.md](./axis-doc-hygiene/SKILL.md) |
| Repo tooling / compose / local-dev / scripts | [axis-script-scope/SKILL.md](./axis-script-scope/SKILL.md) |
| Branch ready for review | [axis-ready-review/SKILL.md](./axis-ready-review/SKILL.md) |
| Open / push update / mark-ready PR | [axis-pull-request/SKILL.md](./axis-pull-request/SKILL.md) |
| Review feedback follow-up | [axis-review-feedback/SKILL.md](./axis-review-feedback/SKILL.md) |

## Typical flow

```text
spec gap     → axis-use-case-spec
module arch  → axis-design-gate → axis-module-architecture
patterns     → axis-module-architecture → axis-module-patterns
non-trivial  → axis-design-gate → implement skill → axis-script-scope (checks)
before review→ axis-ready-review
PR/push action→ axis-pull-request → axis-review-feedback (if needed) → then push/PR
```

Numbered steps in each skill are sequential gates. See [reference.md](./reference.md).
