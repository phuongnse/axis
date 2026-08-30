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
- Compatibility is a product constraint, not an automatic implementation goal. When the owning contract and current product phase require no compatibility, replace the old surface cleanly; do not add shims, dual paths, flags, fallback behavior, or ongoing tests and guidance that keep retired identifiers or concepts alive. Protect finite replacement surfaces with exact positive invariants over the current registry, package, graph, or contract so extra entries fail without naming retired ones; keep the retired-identifier sweep as transient task or review evidence, never as a committed denylist.

## Operating Rules

- Keep product behavior tied to owning use-case acceptance criteria.
- Keep architecture and stack changes aligned with [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) and [docs/TECH_STACK.md](./docs/TECH_STACK.md).
- Keep tests behavior-focused and proportional to risk.
- The pinned engineering-process lifecycle owns specification, planning,
  implementation, verification, independent review, finding loops, and completion.
  Axis product and architecture contracts add domain policy; they do not replace
  lifecycle gates.
- Before non-trivial work, start from `run-change`, read the current phase skill, then
  read only the owning Axis playbooks, contracts, and source.
- Optional work delegation never changes lifecycle ownership. Independent review
  still requires a separate attested read-only actor and context.
- When a required action is outside the repository or user-controlled (for example authentication, consent, client reload/restart, host prerequisites, permissions, approval, or a destructive operation), stop at that boundary, preserve the exact evidence, and ask the user for the action or decision. Do not silently bypass the boundary with disabled security, injected credentials, direct database changes, killed app-managed processes, ad hoc proxies, or indirect evidence.
- Before claiming a slice complete, map its acceptance criteria to current source, test, and runtime evidence. Missing, stale, indirect, or blocked evidence stays `not run` or `blocked`; it is not converted into a pass. MCP runtime work additionally requires the supported client registry and authenticated operation/read-back boundary described in [the MCP playbook](./docs/playbooks/mcp.md#runtime-lifecycle-and-blocker-protocol).

## Verification

Select development and review evidence through `.process/project.json` and
`verify-change`; before review, apply
[docs/playbooks/agent-checklist.md](./docs/playbooks/agent-checklist.md).

<!-- engineering-process:start -->
## Engineering process

For non-trivial delivery work, enter through the managed run-change skill and follow
the processctl lifecycle: start, plan, implement, verify, independent review, finish.

This repository owns product decisions, domain rules, exact argument-array commands,
merge policy, and release authority. The process owns only lifecycle transitions,
managed skills, evidence freshness, and rejection of self-review.

Do not edit .agents/skills or .process/adopt-process.py by hand. They are replaced by
the hash-locked engineering-process adoption in a dependency pull request.
<!-- engineering-process:end -->
