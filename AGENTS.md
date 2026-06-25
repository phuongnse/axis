# Axis - Agent Contract

Axis is a multi-workspace low-code SaaS. The codebase is a modulith with strict service boundaries: each module is shaped like an extractable service from day one.

This file is the high-signal contract for agents. Workflow belongs in repo skills. Detailed reference belongs in the linked owner docs.

## Source Order

When sources conflict, follow this order:

1. Use-case acceptance criteria in `docs/use-cases/**/README.md`
2. This file
3. Focused owner docs and repo skills
4. Same-module code
5. Agent judgment

Never invent IDs, endpoints, events, tables, roles, or product behavior. If legacy code conflicts with specs or this file, surface the conflict.

## P0 Rules

- Spec -> code only; do not rewrite specs to justify shortcuts.
- Do not weaken tests, add `Skip = ...`, mock away behavior under test, bypass auth, skip ACs silently, or mark incomplete work done.
- Domain projects have zero external dependencies.
- Do not make non-trivial changes without a Design Gate dossier through `$axis-design-gate`; high-risk surfaces need user sign-off before code.
- Do not mark a PR ready while triggered verification is failing.
- Intentional P0/P1 shortcuts require `docs/WORKAROUNDS.md` plus a site reference in the same PR.
- Tech-stack changes require explicit user approval and a `docs/TECH_STACK.md` update.

## Boundaries

- Module references may point to `Axis.{OtherModule}.Contracts` only; never to another module's Application or Infrastructure.
- Cross-module work uses Kafka Avro CloudEvents for `*Event`/`*Snapshot`, RabbitMQ/Wolverine for `*Command`/`*Job`/`*SagaStep`, or gRPC proto contracts for sync escape hatches.
- MediatR is intra-module only.
- No shared `DbContext`, cross-module SQL, cross-module aggregate references, or shared repository/unit-of-work implementation.
- Shared kernel projects contain abstractions/primitives only.
- Non-Identity modules validate JWTs locally via JWKS or use Identity gRPC when claims are insufficient; never query Identity tables.
- `Axis.Api` is the REST/OpenAPI gateway for the SPA. Frontend calls only the gateway.

## Implementation Defaults

- Work in layer order: Contracts, Domain, Application, Infrastructure, module entrypoint, `Axis.Api`, Frontend.
- Business failures return `Result` / `Result<T>`; exceptions are for infrastructure failures.
- Minimal API endpoints require authorization unless explicitly public.
- Schema changes use EF Core migrations; `EnsureCreated` is forbidden.
- New cross-module RPC starts with `.proto`; new events start with Avro schema and CloudEvents envelope.
- Behavior/status changes update the owning use-case status. Pure refactor/style/test-only changes do not need token docs edits.
- No new `TODO`, `FIXME`, placeholder, stub, or `NotImplementedException` under `src/`, `tests/`, or `frontend/src/`.

## Skill Routing

Use the focused repo skill instead of reading broad workflow docs:

| Work | Skill |
|---|---|
| Guidance/docs surfaces | `$axis-doc-hygiene` |
| Script/check selection or command docs | `$axis-script-scope` |
| Missing or unclear use-case spec | `$axis-use-case-spec` |
| Use-case implementation | `$axis-use-case-implementation` |
| REST/OpenAPI/API types | `$axis-api-contract` |
| Events, Wolverine, Kafka, RabbitMQ, gRPC, proto | `$axis-cross-module-contract` |
| Frontend feature or SPA caller | `$axis-frontend-feature` |
| Design tokens, primitives, consumer contracts | `$axis-design-system` |
| Design sources, previews, wireframes, Mermaid | `$axis-visual-artifact` |
| Review comments | `$axis-review-feedback` |
| Review readiness | `$axis-ready-review` |
| PR publish/update/mark ready | `$axis-pull-request` |

## Verification

During development, run only the narrow check that proves the surface you changed. Before asking for review, use `$axis-ready-review`; it owns the ready-review verification boundary and reporting shape. CI remains the authoritative full suite before merge.

Verification terminology and enforcement status live in `docs/REVIEW_FINDINGS.md`. Do not call review-only artifacts gates.

## Git

- Branch names: `{feat|fix|docs|refactor|test|chore}/{short-description}`.
- Never push to `main`.
- Commit messages use Conventional Commits, English, and 72 characters or fewer.
- Review fixes stay on the existing PR branch.

## Reference Owners

- Stack and ADRs: `docs/TECH_STACK.md`
- Architecture shape: `docs/ARCHITECTURE.md`
- Use cases and module responsibilities: `docs/use-cases/README.md`
- Enforcement ledger: `docs/REVIEW_FINDINGS.md`
- Current intentional shortcuts: `docs/WORKAROUNDS.md`
- Minimal daily checklist: `docs/playbooks/agent-checklist.md`

## Docs index

The full documentation index lives in `docs/README.md`. Keep this section as an anchor for older links; do not expand it into a second index.
