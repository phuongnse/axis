# Tech Stack

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

Axis keeps the stack intentionally small. Exact versions live in the owning manifests, not this file.

Every approved runtime and dependency must be vendor-supported for the production boundary that uses it. Development-only services, credentials, certificates, and ephemeral keys are adapters for local evidence, never production defaults or substitutes for a production deployment contract.

## Change Rule

Tech-stack changes need explicit user approval before implementation. Update this file in the same change when adding, removing, or replacing a runtime, framework, service, or major library.

The runtime, framework, persistence provider, and framework-integrated libraries must resolve to one vendor-supported target combination. A successful restore, a transitive minimum version, or an unexercised code path is not compatibility evidence. A recovery after failure follows [AGENTS.md § Critical Rules](../AGENTS.md#critical-rules): changing the required owner, runtime/trust boundary, invariant, or evidence merely to continue is a workaround, not a stack fix.

## Baseline

| Area | Approved stack |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core Minimal API, MediatR, FluentValidation. |
| Persistence | EF Core migrations, Npgsql/PostgreSQL, Redis. |
| Search | Server-owned CQRS read providers; PostgreSQL full-text search with `unaccent` and `pg_trgm` is the first adapter. |
| Auth | OpenIddict Authorization Code + PKCE, confidential-browser PAR, refresh and revocation, opaque Redis-backed ASP.NET Core cookie sessions, antiforgery, BCrypt password hashing, MailKit SMTP. Enterprise browsers never own OAuth bearer or refresh tokens. |
| Product BFF | ASP.NET Core .NET 10, YARP direct forwarding, `Duende.AccessTokenManagement.OpenIdConnect` 4, Redis-backed `ITicketStore`, and persistent ASP.NET Core Data Protection keys. |
| Agent integration | .NET 10 LTS local stdio bridge using the official Model Context Protocol C# SDK 1.4.1; typed MCP tools cover authenticated product OpenAPI operations through loopback HTTPS. |
| Observability | Serilog, OpenTelemetry, optional local OTEL/LGTM stack. |
| API contract | OpenAPI via Swashbuckle 10.2.3 and Scalar; SPA types generated with `@hey-api/openapi-ts`. |
| Frontend | React, TypeScript, Vite, TanStack Router, TanStack Query, TanStack Table, TanStack Virtual, React Query Builder, Zustand, react-hook-form, Zod, i18next/react-i18next. |
| UI | Tailwind CSS 4, shadcn components, Base UI-backed shared primitives, lucide-react icons, react-rnd for managed dialog movement and resizing. |
| Testing | xUnit v3, Testcontainers, architecture tests, Vitest, Testing Library, Playwright. |
| Local runtime | Docker Compose with PostgreSQL, Redis, Maildev, API, SPA, and optional observability/e2e profiles. |
| Dependency automation | Renovate. |
| Engineering process | Public `engineering-process` Python distribution and portable `processctl` lifecycle/environment contract. |

## Dependency Version Policy

- The frontend toolchain is one coherent baseline: exact Node in `frontend/.nvmrc` and `frontend/Dockerfile.dev`, its bundled exact npm in `frontend/package.json`, and the portable Node/npm contract in `.process/project.json`.
- Direct frontend dependencies and overrides use exact versions in `frontend/package.json`; `package-lock.json` locks the full resolved graph and `python scripts/axis.py frontend install` is the only supported install path.
- .NET package versions remain centralized and exact in `Directory.Packages.props`; each project commits its generated `packages.lock.json`, and CI restores the full graph in locked mode. GitHub Actions remain digest-pinned.
- Independently versioned product BFFs pin their .NET SDK, direct NuGet packages, container bases, and full restore graph in their own repository; the Axis reference product is the required proving consumer of this supported combination.
- Framework-integrated packages use the asset compiled for Axis's target framework and share its runtime/provider major unless the vendor's published support matrix explicitly owns another combination and an integration test exercises the affected abstraction.
- Renovate is the only automated version proposer. It opens weekly dependency and lockfile updates, requires Dependency Dashboard approval before creating a major-update PR, and raises vulnerability fixes without waiting for approval or the normal schedule. It never bypasses CI or human merge review.
- Pull-request CI audits every changed frontend surface. The scheduled dependency-security workflow audits the locked npm and NuGet graphs daily on the default branch so newly published advisories do not wait for an unrelated code change.
- Version changes update the owning manifest and generated lock or contract artifacts together. Do not add floating ranges, manual transitive edits, compatibility shims, or unapproved or persistent vulnerability exceptions to preserve an obsolete version. Lower-severity findings may use only the documented exact, time-bounded acceptance process.

## Version Owners

- .NET SDK: `global.json`
- Backend packages: `Directory.Packages.props`
- Backend resolved graphs: sibling `packages.lock.json` files for every `src/**` and `tests/**` project
- Frontend direct packages: `frontend/package.json`
- Frontend resolved graph: `frontend/package-lock.json`
- Local container images: `docker-compose.yml`
- OpenAPI contract: `openapi.json`
- Dependency automation: `.github/renovate.json5`
- Scheduled dependency audit: `.github/workflows/dependency-security.yml`
- Reference-product BFF runtime and packages: the independently versioned reference-product repository
- Engineering-process direct public pin: `requirements/process.in`
- Engineering-process compiled package graph and hashes: `requirements/process.txt`
- Engineering-process CI bootstrap action: full governed release commit in `.github/workflows/build-and-test.yml` and `.github/workflows/dependency-security.yml`
- Portable environment and finite profiles: `.process/project.json`
- Process distribution resources: `.process/process.lock`
- Process adoption materialization: `.process/adopt-process.py` and the installed target distribution
