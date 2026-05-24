# Architectural Workarounds Inventory

> [← docs/README.md](README.md) · [← CLAUDE.md](../CLAUDE.md) · [← TECH_STACK.md](TECH_STACK.md)

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
2. Add a code comment at the workaround site: `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>`. The `<slug>` is the lowercased, hyphenated heading of your entry. The drift check (`scripts/check-doc-drift.sh`) verifies every `WORKAROUND:` comment links to a real section.
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
- **Cleanup trigger**: specific event — "when PR for E04 Contracts lands", "when Kafka schema registry is provisioned in prod", "when feature flag X is removed"
- **Owner**: GitHub handle (optional, but helpful)
- **Added**: PR #N (link)
```

---

## Active workarounds

### formbuilder-consumes-workflowengine-domain

- **Location**: `src/Modules/FormBuilder/Axis.FormBuilder.Infrastructure/Handlers/FormStepReachedHandler.cs`
- **Violates**: CLAUDE.md P0 — "No project reference from `Axis.{ModuleA}.*` to `Axis.{ModuleB}.*` except to `Axis.{ModuleB}.Contracts`." `FormStepReachedHandler` imports `Axis.WorkflowEngine.Domain.Events.FormStepReached` to react to it in-process.
- **Why it exists**: WorkflowEngine has not yet defined its `Axis.WorkflowEngine.Contracts` project with Avro schemas + gRPC service. Cross-module event flow currently uses in-process Wolverine pub instead of Kafka.
- **Cleanup trigger**: PR that adds `Axis.WorkflowEngine.Contracts` with `FormStepReachedEvent` Avro schema and switches the publish path to Kafka. Tracked under "E06 Service-boundary retrofit ⏳" in `docs/PROGRESS.md`.
- **Added**: pre-dates the architecture tests; surfaced when [`chore/architecture-fitness-and-workarounds-inventory`](../tests/Architecture/Axis.Architecture.Tests/ModuleBoundaryTests.cs) ran for the first time.

### workflowengine-consumes-formbuilder-domain

- **Location**:
  - `src/Modules/WorkflowEngine/Axis.WorkflowEngine.Infrastructure/Handlers/FormTaskSubmittedHandler.cs`
  - `src/Modules/WorkflowEngine/Axis.WorkflowEngine.Infrastructure/Handlers/FormTaskExpiredHandler.cs`
- **Violates**: CLAUDE.md P0 — same rule as above. Both handlers import `Axis.FormBuilder.Domain.Events.FormTaskSubmitted` / `FormTaskExpired` to react in-process.
- **Why it exists**: FormBuilder has not yet defined `Axis.FormBuilder.Contracts` with Avro schemas. The form-task lifecycle events stay as in-process domain events instead of Kafka integration events.
- **Cleanup trigger**: PR that adds `Axis.FormBuilder.Contracts` with `FormTaskSubmittedEvent` / `FormTaskExpiredEvent` Avro schemas and converts the consumers to read from Kafka topics. Tracked under "E05 Service-boundary retrofit ⏳" in `docs/PROGRESS.md`.
- **Added**: pre-dates the architecture tests; surfaced when this inventory was created.

