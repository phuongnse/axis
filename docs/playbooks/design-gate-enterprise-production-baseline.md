# Design Gate: Enterprise Production Baseline

> **Navigation**: [docs/README.md](../README.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [docs/playbooks/design-gate.md](./design-gate.md) · [AGENTS.md](../../AGENTS.md)

## Risk and scope

This is a full high-risk Design Gate for the project-wide engineering baseline. It changes the default decision standard applied by agents, use cases, architecture, stack, verification, and later product integrations. It does not claim that every target capability already exists and does not implement the reference-product authentication architecture.

## Governing rules

- [AGENTS.md § Source Order](../../AGENTS.md#source-order) owns decision precedence; the enterprise-production baseline must therefore be present in the agent contract and reflected by every lower owner.
- [docs/PLATFORM_STRATEGY.md § Product Position](../PLATFORM_STRATEGY.md#product-position) owns the enterprise product target and public platform/product boundary.
- [docs/playbooks/design-gate.md § Dossier](./design-gate.md#dossier) owns mandatory pre-code reasoning; production fitness must be decided there rather than inferred after implementation.
- [docs/playbooks/agent-checklist.md § Acceptance Coverage](./agent-checklist.md#acceptance-coverage) requires honest boundary evidence; missing production evidence cannot become a pass.
- [docs/ENFORCEMENT.md § Status](../ENFORCEMENT.md#status) distinguishes deterministic enforcement from review-only judgment.

## Root cause and blast radius

Axis already targets an enterprise platform, but the contract does not consistently distinguish incremental capability delivery from production-grade implementation. Some current wording can be read as allowing temporary foundations, and the Design Gate does not require an explicit production-fitness decision.

The pre-edit sweep covers:

```text
AGENTS.md
docs/PLATFORM_STRATEGY.md
docs/ARCHITECTURE.md
docs/TECH_STACK.md
docs/ENFORCEMENT.md
docs/README.md
docs/playbooks/design-gate.md
docs/playbooks/agent-checklist.md
.agents/skills/axis-design-gate/SKILL.md
.agents/skills/axis-use-case-spec/
current use-case and Design Gate maturity decisions
```

## Enterprise-production decision

Axis delivers capabilities incrementally, but every implemented slice is production-grade within its declared scope. A wave may leave a separate capability unimplemented; it may not knowingly ship a temporary security, tenancy, data, API, persistence, deployment, operability, accessibility, or evidence contract that must be replaced before production.

Every non-trivial Design Gate must classify applicable production concerns and prove or explicitly reject them from the slice: security/privacy, authorization/isolation, data lifecycle and migration, failure/recovery and concurrency, deployment/configuration/secrets, observability/support, performance/capacity, accessibility/localization, supply-chain maintenance, and compatibility/rollback. `N/A` requires an owning-contract reason. A required concern that is absent makes the slice Blocked; it is not a deferred follow-up.

Local, test, and reference deployments may use different addresses, credentials, certificates, data, and adapters, but must preserve production semantics and trust boundaries. A lower-cost environment cannot disable or substitute authentication, authorization, isolation, TLS validation, migrations, concurrency, recovery, or required runtime evidence.

The reference product is production-representative acceptance evidence for public Axis contracts. It is independently deployable and must use the architecture appropriate to an enterprise business application; demonstration-only identity, hidden setup, direct database access, or browser evidence that stops before the real trust boundary is invalid.

No production consumer or production data exists for the current cutover surfaces. This permits clean replacement and migration-history reset where separately approved; it never lowers the production quality required of the replacement.

## Retirement and compatibility

Clean cutover applies to ambiguous maturity guidance because it has no supported external contract. Replace it with the enterprise-production rule in its single owners; do not add dual quality modes, temporary architecture flags, or compatibility guidance. Legitimate staged capabilities remain explicit out-of-scope items with an owning use case or delivery wave.

## Contract and architecture decision

N/A for wire and database shape because this baseline changes decision governance only. Modular-monolith boundaries, public REST/OpenAPI ownership, module-owned persistence, and rejected default event sourcing remain unchanged. Later BFF work must re-enter the auth and stack Design Gate under this baseline.

## Verification and routing

- Development proof: `python scripts/axis.py check repo-skills`, `python scripts/axis.py check use-case-docs`, `python scripts/axis.py check doc-link-targets`, and `python scripts/axis.py check doc-drift`.
- Review proof: sweep current guidance for conflicting maturity claims, map the changed workflow contract to [docs/ENFORCEMENT.md](../ENFORCEMENT.md), then run the documentation-triggered review-readiness checks.
- `$axis-design-gate` owns the baseline decision; `$axis-doc-hygiene` owns single-source edits; `$axis-module-architecture` confirms no module boundary is introduced. Integration remains primary-owned because the change alters shared decision precedence and the next auth Design Gate.
