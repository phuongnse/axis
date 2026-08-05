# Axis - Agent Contract

This file is the high-signal contract for agents. Keep workflow details in focused owner docs.

## Source Order

1. Use-case acceptance criteria under [docs/use-cases/README.md](./docs/use-cases/README.md)
2. This file
3. Focused owner docs
4. Same-module code
5. Agent judgment

Do not invent IDs, endpoints, tables, or product behavior. If code and docs conflict, surface the conflict.

## Critical Rules

- Axis targets enterprise production. Delivery may be incremental, but every implemented slice must be production-grade within its declared scope under [docs/PLATFORM_STRATEGY.md](./docs/PLATFORM_STRATEGY.md#enterprise-production-baseline); a required production concern cannot be deferred or replaced by a temporary foundation.
- Spec -> code only; no intentional shortcuts.
- Keep tests and acceptance evidence honest; do not skip, weaken, bypass, or mark incomplete work done.
- Domain projects have zero external dependencies.
- Non-trivial changes need a [docs/playbooks/design-gate.md](./docs/playbooks/design-gate.md) dossier; high-risk surfaces need user sign-off before code.
- Keep database schema changes migration-backed and reviewable.
- Tech-stack changes need explicit approval and a [docs/TECH_STACK.md](./docs/TECH_STACK.md) update.
- Before taking an alternate path after a failure, compare it with the owning contract's owner, required boundary, invariants, and evidence. If the path changes any of them merely to keep progressing instead of repairing the root cause, it is a workaround: stop and reopen the Design Gate rather than implement it.
- Compatibility is a product constraint, not an automatic implementation goal. When the owning contract and current product phase require no compatibility, replace the old surface cleanly; do not add shims, dual paths, flags, fallback behavior, or ongoing tests and guidance that keep retired identifiers or concepts alive.

## Operating Rules

- Keep product behavior tied to owning use-case acceptance criteria.
- Keep architecture and stack changes aligned with [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) and [docs/TECH_STACK.md](./docs/TECH_STACK.md).
- Keep tests behavior-focused and proportional to risk.
- Repeatable workflows live in [`.agents/skills/`](./.agents/skills/README.md).
- Before non-trivial work, read the matching `SKILL.md` and [`.agents/skills/reference.md`](./.agents/skills/reference.md), then follow numbered steps in order.
- After selecting the workflow owner, proactively delegate eligible independently ownable work units under [`.agents/skills/reference.md` § Agent routing](./.agents/skills/reference.md#agent-routing) before execution and whenever a routing re-evaluation trigger occurs; model choice never changes skill ownership or gates.
- `$axis-*` aliases in docs map to `.agents/skills/<name>/SKILL.md`. Do not skip workflow gates or defer them into PR follow-ups unless the user explicitly approved that deferral.
- When a required action is outside the repository or user-controlled (for example authentication, consent, client reload/restart, host prerequisites, permissions, approval, or a destructive operation), stop at that boundary, preserve the exact evidence, and ask the user for the action or decision. Do not silently bypass the boundary with disabled security, injected credentials, direct database changes, killed app-managed processes, ad hoc proxies, or indirect evidence. Follow the [blocker and completion protocol](./.agents/skills/reference.md#blocker-and-completion-protocol).
- Before claiming a slice complete, map its acceptance criteria to current source, test, and runtime evidence. Missing, stale, indirect, or blocked evidence stays `not run` or `blocked`; it is not converted into a pass. MCP runtime work additionally requires the supported client registry and authenticated operation/read-back boundary described in [the MCP playbook](./docs/playbooks/mcp.md#runtime-lifecycle-and-blocker-protocol).

## External Skills

Axis repository skills own the development lifecycle. External skills may supplement reasoning and diagnostics, but must not create parallel specs, plans, review gates, verification commands, or Git/PR workflows. When an external skill overlaps a `$axis-*` owner, the Axis owner takes precedence.

## Verification

Select development and review verification under [`.agents/skills/reference.md` § Change-driven scope](./.agents/skills/reference.md#change-driven-scope); before review, apply [docs/playbooks/agent-checklist.md](./docs/playbooks/agent-checklist.md).
