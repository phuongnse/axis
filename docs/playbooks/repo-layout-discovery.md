# Repo layout discovery (agents)

> **Navigation**: [← docs/README.md](../README.md) · [← agent-checklist.md](./agent-checklist.md) · [← CLAUDE.md](../../CLAUDE.md)

**Single source** for how CI maps **code paths → docs/config** without hand-maintained lists in `check-doc-drift.sh`. Shared logic lives in [`scripts/axis_repo.py`](../../scripts/axis_repo.py). Agents **run the checks locally** before push; CI runs the same commands inside **Doc drift** and **dotnet test**.

---

## Auto-discovered (do not duplicate lists elsewhere)

| What changes | How CI knows | Agent fix when check fails |
|--------------|--------------|----------------------------|
| `src/Modules/{Module}/` | Folder name → `docs/use-cases/{slug}/` ([`doc_drift_domains.py`](../../scripts/doc_drift_domains.py)) | Add domain folder + use-case docs in **same PR** as module code |
| `src/Axis.Api/Endpoints/*Endpoints.cs` | `using Axis.{Module}.Application` → same domain as module | Same as row above |
| `*Event.avsc` under `Contracts/Schemas/` | [`register-avro-schemas.sh`](../../scripts/register-avro-schemas.sh) globs `*Event.avsc` | No script edit; optional local: `./scripts/register-avro-schemas.sh` |
| `Contracts/Protos/*.proto` | [`sync_buf_yaml.py --check`](../../scripts/sync_buf_yaml.py) vs `buf.yaml` | `python3 scripts/sync_buf_yaml.py --write` then `buf lint` |
| `*KafkaTopics.cs` constants | [`check_kafka_wiring.py`](../../scripts/check_kafka_wiring.py) vs `Program.cs` | Add `PublishAndListenWithAvro` + `PublishLocally` for new `{Class}.{Const}` |
| Use-case `## Purpose` / new folder under domain | [`regenerate-domain-readme-index.py --check`](../../scripts/regenerate-domain-readme-index.py) | `python3 scripts/regenerate-domain-readme-index.py` |
| `docker-compose.yml` services / ports | [`check-local-dev-docs.py`](../../scripts/check-local-dev-docs.py) | Update [local-dev.md](./local-dev.md) |
| Module folders under `src/Modules/` | [`Conventions.ModuleNames`](../../tests/Architecture/Axis.Architecture.Tests/Conventions.cs) at test run | Add module projects to architecture test csproj when layers exist |
| `OrganizationVerifiedHandler` in a module | [`TenantProvisioningConventionTests`](../../tests/Architecture/Axis.Architecture.Tests/TenantProvisioningConventionTests.cs) vs `TenantModuleNames.All` | Add const + entry in `TenantModuleNames` ([`TenantProvisioningContracts.cs`](../../src/Modules/Identity/Axis.Identity.Contracts/TenantProvisioningContracts.cs)) |

**Slug override (rare):** when module folder name ≠ `docs/use-cases/` folder (only `Identity` → `identity-access` today), add one line to `MODULE_DOMAIN_SLUG_OVERRIDES` in [`axis_repo.py`](../../scripts/axis_repo.py).

**Cross-cutting doc rules (rare):** paths that are not owned by a single module (e.g. auth `AppShell`, `OrganizationVerifiedHandler`) stay in `EXTRA_CODE_TO_DOC_RULES` inside [`doc_drift_domains.py`](../../scripts/doc_drift_domains.py).

---

## Still manual (CI catches omissions)

| Item | Why not generated | Agent must |
|------|-------------------|------------|
| Topical **sections** in domain README (`GROUPS` in [`regenerate-domain-readme-index.py`](../../scripts/regenerate-domain-readme-index.py)) | Product grouping, not filesystem | Optional: add slug to `GROUPS`; **unlisted use cases appear under “Other”** (safe) |
| `Program.cs` Wolverine `PublishAndListenWithAvro<T>` lines | Needs event **type** + serializer | Wire each new `*KafkaTopics` const (see check above) |
| `TenantModuleNames` string ids | Kafka contract between modules | Add module slug when adding `OrganizationVerifiedHandler` |
| Architecture test **project references** | MSBuild needs explicit refs | Reference new `Axis.{Module}.*` projects in `Axis.Architecture.Tests.csproj` when testing that module |
| Use-case **spec** content (AC, wireframes) | Source of truth | Follow [USE_CASE_TEMPLATE.md](../use-cases/USE_CASE_TEMPLATE.md) |

---

## One command before push (layout + docs)

When `src/`, `tests/`, or `docs/use-cases/` change:

```bash
./scripts/check-doc-drift.sh
```

That runs (among others): `doc_drift_domains.py --validate`, domain doc rules on the PR diff, `sync_buf_yaml.py --check`, `check_kafka_wiring.py --check`, `regenerate-domain-readme-index.py --check`, `check-use-case-docs.py`, link targets, local-dev sync.

When only validating discovery (no PR diff yet):

```bash
python3 scripts/doc_drift_domains.py --validate
python3 scripts/doc_drift_domains.py --list    # debug: code → docs rules
python3 scripts/sync_buf_yaml.py --check
python3 scripts/check_kafka_wiring.py --check
python3 scripts/regenerate-domain-readme-index.py --check
```

---

## Agent checklists

### A — New module (`src/Modules/NewModule/`)

- [ ] Create `docs/use-cases/{kebab-slug}/README.md` (or add override in `axis_repo.py` if slug ≠ `pascal-to-kebab(Module)`).
- [ ] Add use-case folder(s) under that domain as specs are written.
- [ ] If tenant provisioning: add `OrganizationVerifiedHandler` + update `TenantModuleNames` in Identity.Contracts.
- [ ] If Kafka events: add `*Event.avsc`, `*KafkaTopics.cs` const, wire `Program.cs` (kafka check).
- [ ] If gRPC: add `Contracts/Protos/*.proto`, run `python3 scripts/sync_buf_yaml.py --write`, `buf lint`.
- [ ] Reference module projects in `tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj` when fitness tests should load them.
- [ ] Run `./scripts/check-doc-drift.sh` and full `dotnet test` on `Axis.sln`.

### B — New use case (folder under `docs/use-cases/{domain}/{slug}/`)

- [ ] Copy layout from [USE_CASE_TEMPLATE.md](../use-cases/USE_CASE_TEMPLATE.md).
- [ ] Run `python3 scripts/regenerate-domain-readme-index.py` (updates domain `## Use Cases` table).
- [ ] Update `> **Implementation status**` in the use-case file when shipping code.
- [ ] Optional: add slug to `GROUPS` in `regenerate-domain-readme-index.py` for a topical section (else **Other**).
- [ ] `./scripts/check-doc-drift.sh`.

### C — New REST surface (`*Endpoints.cs` or handler in existing module)

- [ ] Spec under correct domain in `docs/use-cases/` (drift enforces same-PR doc touch on module/API change).
- [ ] `Map*Endpoints` in `Program.cs` ([process.md § Host wiring](./process.md)).
- [ ] Domain README: set API row to ⚠️ or ✅ when endpoints ship (not `| API | ⏳`).
- [ ] Handler tests: `*Handler.cs` → `*HandlerTests.cs` (drift).
- [ ] `./scripts/check-doc-drift.sh`.

### D — New Kafka event

- [ ] `Contracts/Schemas/{Name}Event.avsc` + generated/hand-written record type.
- [ ] `public const string` on module `*KafkaTopics.cs` (topic `axis.{module}.{kebab-event}`).
- [ ] `Program.cs`: `PublishAndListenWithAvro<...>` and `PublishLocally<...>`.
- [ ] Consumer handler in subscribing module if cross-module.
- [ ] `python3 scripts/check_kafka_wiring.py --check`.

### E — `docker-compose.yml` change

- [ ] Update [local-dev.md](./local-dev.md) (service names/ports are checked against compose).

---

## Rules (P0/P1 for agents)

1. **Do not** add parallel hardcoded module/endpoint lists to `check-doc-drift.sh` — extend [`axis_repo.py`](../../scripts/axis_repo.py) / the dedicated script, then wire `--check` into drift.
2. **Do not** skip `docs/use-cases/` when changing `src/Modules/` or `*Endpoints.cs` — drift fails by design.
3. **Do** run `regenerate-domain-readme-index.py` after changing use-case titles/Purpose or adding a use-case folder.
4. **Do** treat `TenantModuleNames` and `Program.cs` Kafka wiring as contract edits — architecture + kafka checks must pass.
5. **Chore PRs** that touch module code still need a minimal accurate doc touch in the matching domain ([agent-checklist § Chore/style PRs](./agent-checklist.md)).

---

## See also

| Doc | Topic |
|-----|--------|
| [agent-checklist.md](./agent-checklist.md) | Gates 0–3, domain map summary |
| [process.md](./process.md) | Layer order, host wiring, deferred follow-ups |
| [patterns.md § gRPC](./patterns.md) | Buf, proto layout |
| [Architecture tests README](../../tests/Architecture/Axis.Architecture.Tests/README.md) | Fitness tests + new module |
| [CONTRIBUTING.md](../../CONTRIBUTING.md) | Short pre-push pointer |
