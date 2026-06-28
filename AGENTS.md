# Axis - Agent Contract

This file is the high-signal contract for agents. Keep workflow details in focused owner docs.

## Source Order

1. Use-case acceptance criteria under [docs/use-cases/README.md](./docs/use-cases/README.md)
2. This file
3. Focused owner docs
4. Same-module code
5. Agent judgment

Do not invent IDs, endpoints, tables, or product behavior. If code and docs conflict, surface the conflict.

## P0 Rules

- Spec -> code only; no intentional shortcuts.
- Keep tests and acceptance evidence honest; do not skip, weaken, bypass, or mark incomplete work done.
- Domain projects have zero external dependencies.
- Non-trivial changes need a [docs/playbooks/design-gate.md](./docs/playbooks/design-gate.md) dossier; high-risk surfaces need user sign-off before code.
- Keep database schema changes migration-backed and reviewable.
- Tech-stack changes need explicit approval and a [docs/TECH_STACK.md](./docs/TECH_STACK.md) update.

## Operating Rules

- Keep product behavior tied to owning use-case acceptance criteria.
- Keep architecture and stack changes aligned with [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) and [docs/TECH_STACK.md](./docs/TECH_STACK.md).
- Keep tests behavior-focused and proportional to risk.
- Repeatable workflows live in [`.cursor/skills/`](./.cursor/skills/README.md). Before non-trivial work, read the matching `SKILL.md` and follow it. `$axis-*` in docs is an alias for that path.

## Verification

During development, run the narrow check that proves the surface changed. Before review, run triggered verification from [docs/playbooks/agent-checklist.md](./docs/playbooks/agent-checklist.md).
