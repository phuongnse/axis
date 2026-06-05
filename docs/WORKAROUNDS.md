# Architectural Workarounds Inventory

> **Navigation**: [← docs/README.md](./README.md) · [← CLAUDE.md](../CLAUDE.md) · [← TECH_STACK.md](./TECH_STACK.md)

## Why this file exists

Every project accumulates **shortcuts** — code that knowingly violates a stated architectural rule because the proper solution is blocked (infra not ready, scope too big, dependency not in place). Workarounds are not bad; they let us ship. **Invisible** workarounds are bad — they silently become permanent debt and the next agent (human or AI) treats them as the design.

This file makes workarounds visible. Every entry has:

- **Where it lives** (file path + line, or assembly + class).
- **Which rule it violates** (CLAUDE.md P0/P1, an ADR, or a feature spec).
- **Why it exists** (the blocker).
- **Cleanup trigger** (the specific event that enables proper fix).
- **Owner** (optional — who knows the most about this).

Without these five fields, the entry is just noise. Don't add an entry you can't fill out.

## How to use this file

### Adding a workaround

You add a workaround when you ship code that **knowingly** violates a rule. Steps:

1. Open this file. Add an entry under **Active workarounds** using the template below.
2. Add a code comment at the workaround site: `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>`. The `<slug>` is the lowercased, hyphenated heading of your entry. The drift check (`python scripts/axis.py check doc-drift`) verifies every `WORKAROUND:` comment links to a real section.
3. If the workaround is detected by an architecture fitness test, add it to the test's allow-list (e.g. `KnownBoundaryWorkarounds` in `tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs`). Reference this file from the test comment.
4. In the PR description, mention the new workaround so reviewers see it.

### Resolving a workaround

When the cleanup trigger fires and you remove the workaround:

1. **Delete the entry** from **Active workarounds**. Git history is the audit trail — `git log -p docs/WORKAROUNDS.md` shows when and why each entry was removed; no need for an in-file archive.
2. Remove the `// WORKAROUND:` code comment at the violation site.
3. Remove the test allow-list entry (the test will catch this for you — it fails with "stale entries: …" pointing at the exact line to delete).
4. **If the resolution taught a durable lesson** (e.g. "centralised X in the gateway breaks ADR-Y"), capture it in `docs/playbooks/patterns.md` as a pattern or anti-pattern, or amend the relevant ADR. The lesson belongs where future readers will find it; WORKAROUNDS.md is for *current* debt, not history.
5. If the workaround has lived longer than 6 months without its trigger firing, the entry should be **revisited** during the next architecture audit — either the trigger needs to be re-stated, or the workaround needs to be promoted into the design (ADR amendment).

### Reviewing

- **Per-PR**: the PR template asks "did this PR introduce or resolve any workaround?"
- **Periodic**: every ~10 PRs (or once a sprint), scan the **Active workarounds** section. For each entry whose trigger has fired but workaround remains, file a task.

## Entry template

```markdown
### <short-name-as-slug>

- **Location**: `path/to/file.cs` (or `Axis.Foo.Infrastructure.SomeClass`)
- **Violates**: CLAUDE.md P0 — quote the specific rule, or "ADR-NNN", or "F0N spec line"
- **Why it exists**: 1-3 sentences. What's the blocker?
- **Cleanup trigger**: specific event — "when PR for workflow-builder Contracts lands", "when Kafka schema registry is provisioned in prod", "when feature flag X is removed"
- **Owner**: GitHub handle (optional, but helpful)
- **Added**: <date or short context>
```

---

## Active workarounds

### org-hard-delete-modulith-cancellers

- **Location**: `src/Modules/Identity/Axis.Identity.Infrastructure/Messaging/OrganizationHardDeleteHandler.cs`
- **Violates**: CLAUDE.md P0 — cross-module work must use Kafka events or RabbitMQ commands, not in-process calls into another module's infrastructure
- **Why it exists**: organization-management [organization deletion](./README.md) hard-delete must cancel Workflow Engine executions and Form Builder pending tasks before dropping tenant schemas. Modulith composition registers `IOrganizationExecutionCanceller` and `IOrganizationFormTaskCanceller` at `Axis.Api` startup; Identity invokes them synchronously inside the scheduled `OrganizationHardDeleteJob` handler. RabbitMQ command contracts and outbox handlers are not yet defined for these two steps.
- **Cleanup trigger**: When Workflow Engine and Form Builder expose versioned `*Command` handlers for org-scoped cancellation (ADR-024/025) and module extraction wiring no longer shares a process, replace direct canceller calls with Wolverine `IMessageBus` publish and remove the shared canceller interfaces from the hard-delete path.
