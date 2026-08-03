# Tech Stack

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

Axis keeps the stack intentionally small. Exact versions live in the owning manifests, not this file.

## Change Rule

Tech-stack changes need explicit user approval before implementation. Update this file in the same change when adding, removing, or replacing a runtime, framework, service, or major library.

## Baseline

| Area | Approved stack |
|---|---|
| Backend | .NET 8, ASP.NET Core Minimal API, MediatR, FluentValidation. |
| Persistence | EF Core migrations, Npgsql/PostgreSQL, Redis. |
| Search | Server-owned CQRS read providers; PostgreSQL full-text search with `unaccent` and `pg_trgm` is the first adapter. |
| Auth | OpenIddict Authorization Code + PKCE, BCrypt password hashing, MailKit SMTP. |
| Agent integration | .NET 8 local stdio bridge using the official Model Context Protocol C# SDK 1.4.1; typed MCP tools cover authenticated product OpenAPI operations through loopback HTTPS. |
| Observability | Serilog, OpenTelemetry, optional local OTEL/LGTM stack. |
| API contract | OpenAPI via Swashbuckle/Scalar; SPA types generated with `@hey-api/openapi-ts`. |
| Frontend | React, TypeScript, Vite, TanStack Router, TanStack Query, TanStack Table, TanStack Virtual, React Query Builder, Zustand, react-hook-form, Zod, i18next/react-i18next. |
| UI | Tailwind CSS 4, shadcn components, Base UI-backed shared primitives, lucide-react icons, react-rnd for managed dialog movement and resizing. |
| Testing | xUnit v3, Testcontainers, architecture tests, Vitest, Testing Library, Playwright. |
| Local runtime | Docker Compose with PostgreSQL, Redis, Maildev, API, SPA, and optional observability/e2e profiles. |
| Dependency automation | Renovate. |

## Dependency Version Policy

- The frontend toolchain is one coherent baseline: exact Node in `frontend/.nvmrc` and `frontend/Dockerfile.dev`, its bundled exact npm in `frontend/package.json`, and the portable Node pin in `scripts/axis_setup.py`.
- Direct frontend dependencies and overrides use exact versions in `frontend/package.json`; `package-lock.json` locks the full resolved graph and `python scripts/axis.py frontend install` is the only supported install path.
- .NET package versions remain centralized and exact in `Directory.Packages.props`. GitHub Actions remain digest-pinned.
- Renovate is the only automated version proposer. It opens weekly dependency and lockfile updates, requires Dependency Dashboard approval before creating a major-update PR, and raises vulnerability fixes without waiting for approval or the normal schedule. It never bypasses CI or human merge review.
- Pull-request CI audits every changed frontend surface. The scheduled dependency-security workflow audits the locked npm and NuGet graphs daily on the default branch so newly published advisories do not wait for an unrelated code change.
- Version changes update the owning manifest and generated lock or contract artifacts together. Do not add floating ranges, manual transitive edits, compatibility shims, or unapproved or persistent vulnerability exceptions to preserve an obsolete version. Lower-severity findings may use only the documented exact, time-bounded acceptance process.

## Version Owners

- .NET SDK: `global.json`
- Backend packages: `Directory.Packages.props`
- Frontend direct packages: `frontend/package.json`
- Frontend resolved graph: `frontend/package-lock.json`
- Local container images: `docker-compose.yml`
- OpenAPI contract: `openapi.json`
- Dependency automation: `.github/renovate.json5`
- Scheduled dependency audit: `.github/workflows/dependency-security.yml`
