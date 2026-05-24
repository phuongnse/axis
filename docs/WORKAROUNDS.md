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

1. Move the entry from **Active workarounds** to **Resolved workarounds**. Add a "Resolved in" line with the PR number.
2. Remove the `WORKAROUND:` code comment.
3. Remove the test allow-list entry (the test will catch this for you — it fails with "stale entries: …").
4. If the workaround entry is older than 6 months and the cleanup trigger never fired, the entry should be **revisited** during the next architecture audit — either the trigger needs to be re-stated, or the workaround needs to be promoted into the design (ADR amendment).

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

---

## Resolved workarounds

### central-tenant-schema-provisioner

- **Location** (removed): `src/Axis.Api/Infrastructure/TenantSchemaProvisioner.cs`, `src/Axis.Api/Infrastructure/Handlers/ProvisionTenantHandler.cs`, `src/Shared/Axis.Shared.Application/Tenancy/ProvisionTenantMessage.cs`, `src/Shared/Axis.Shared.Application/Tenancy/ITenantSchemaProvisioner.cs`
- **Violated**: ADR-010 — "extraction is a redeploy, not a refactor." The central provisioner sat in `Axis.Api` and directly imported `DataModelingDbContext`, `FormBuilderDbContext`, `WorkflowBuilderDbContext`, `WorkflowEngineDbContext` so that extracting any of those modules required moving the provisioning code AND wiring a new Kafka consumer.
- **Why it existed**: written for E01 US-003 before ADR-010 and ADR-019 (Avro + Schema Registry) were defined. Tenant provisioning needed *some* implementation and Kafka infra wasn't in place yet.
- **Cleanup trigger**: PR that wired Avro + Schema Registry for Identity → fired in PR #93.
- **Resolved in**: PR #93. Replaced with per-module `OrganizationVerifiedHandler` consuming `OrganizationVerifiedEvent` over Kafka.
