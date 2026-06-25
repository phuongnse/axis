# Axis - Agent Contract

Axis is currently a narrow product slice: standalone email/password user registration, email verification, PKCE token exchange, and an account dashboard.

This file is the high-signal contract for agents. Keep workflow details in repo skills or short playbooks.

## Source Order

1. Use-case acceptance criteria in `docs/use-cases/**/README.md`
2. This file
3. Focused owner docs and repo skills
4. Same-module code
5. Agent judgment

Do not invent IDs, endpoints, tables, or product behavior. If code and docs conflict, surface the conflict.

## P0 Rules

- Spec -> code only; do not rewrite specs to justify shortcuts.
- Do not weaken tests, add `Skip = ...`, mock away behavior under test, bypass auth, skip ACs silently, or mark incomplete work done.
- Domain projects have zero external dependencies.
- Non-trivial changes need a Design Gate dossier through `$axis-design-gate`; high-risk surfaces need user sign-off before code.
- Schema changes use EF Core migrations; `EnsureCreated` is forbidden.
- Intentional shortcuts require `docs/WORKAROUNDS.md` plus a site reference in the same PR.
- Tech-stack changes require explicit user approval and a `docs/TECH_STACK.md` update.

## Current Boundaries

- `Axis.Api` is the REST/OpenAPI gateway for the SPA.
- The only module is Identity: `Domain -> Application -> Infrastructure -> Axis.Api -> frontend`.
- Frontend calls only `Axis.Api` and uses generated OpenAPI types.
- `Axis.Shared.*` contains shared primitives, CQRS/validation helpers, observability, and other truly cross-cutting code only.
- Removed capabilities are not current scope; reintroduce them only through a new use-case spec, tests, and docs.

## Implementation Defaults

- Business failures return `Result` / `Result<T>`; exceptions are for infrastructure failures.
- Minimal API endpoints require authorization unless explicitly public.
- Keep tests behavior-focused and proportional to risk.
- Behavior/status changes update the owning use-case status.
- No new `TODO`, `FIXME`, placeholder, stub, or `NotImplementedException` under `src/`, `tests/`, or `frontend/src/`.

## Skill Routing

| Work | Skill |
|---|---|
| Guidance/docs surfaces | `$axis-doc-hygiene` |
| Script/check selection or command docs | `$axis-script-scope` |
| Missing or unclear use-case spec | `$axis-use-case-spec` |
| Use-case implementation | `$axis-use-case-implementation` |
| REST/OpenAPI/API types | `$axis-api-contract` |
| Frontend feature or SPA caller | `$axis-frontend-feature` |
| Design tokens, primitives, visual artifacts | `$axis-design-system` / `$axis-visual-artifact` |
| Review comments or review readiness | `$axis-review-feedback` / `$axis-ready-review` |
| PR publish/update/mark ready | `$axis-pull-request` |

## Verification

During development, run the narrow check that proves the surface changed. Before review, run the triggered verification from `$axis-ready-review`.

## Reference Owners

- Stack: `docs/TECH_STACK.md`
- Architecture: `docs/ARCHITECTURE.md`
- Use cases: `docs/use-cases/README.md`
- Enforcement ledger: `docs/REVIEW_FINDINGS.md`
- Intentional shortcuts: `docs/WORKAROUNDS.md`
