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
| Auth | OpenIddict Authorization Code + PKCE, BCrypt password hashing, MailKit SMTP. |
| Observability | Serilog, OpenTelemetry, optional local OTEL/LGTM stack. |
| API contract | OpenAPI via Swashbuckle/Scalar; SPA types generated with `openapi-typescript`. |
| Frontend | React, TypeScript, Vite, TanStack Router, TanStack Query, Zustand, react-hook-form, Zod. |
| UI | Tailwind CSS, shadcn/Base UI primitives, lucide-react icons. |
| Testing | xUnit, Testcontainers, architecture tests, Vitest, Testing Library, Playwright. |
| Local runtime | Docker Compose with PostgreSQL, Redis, Maildev, API, SPA, and optional observability/e2e profiles. |

## Version Owners

- .NET SDK: `global.json`
- Backend packages: `Directory.Packages.props`
- Frontend packages: `frontend/package.json`
- Local container images: `docker-compose.yml`
- OpenAPI contract: `openapi.json`
