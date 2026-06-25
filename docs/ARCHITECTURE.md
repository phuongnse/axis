# Architecture

> **Navigation**: [docs](./README.md) · [AGENTS.md](../AGENTS.md)

Axis currently ships one module behind one API gateway.

## Runtime Shape

| Layer | Runtime |
|---|---|
| Web | React SPA in `frontend/` |
| Gateway | `src/Axis.Api` REST/OpenAPI gateway |
| Module | `src/Modules/Identity` |
| Data | PostgreSQL database `axis_identity` |
| Cache / token support | Redis |
| Email | SMTP, Maildev in local development |
| Observability | Serilog + OpenTelemetry exporter configuration |

## Source Boundaries

```text
Axis.Identity.Domain
  -> Axis.Identity.Application
  -> Axis.Identity.Infrastructure
  -> Axis.Api
  -> frontend
```

`frontend/` calls only `Axis.Api`. `Axis.Api` calls Identity Application through MediatR and composes Identity Infrastructure at startup.

## Authentication

Identity uses OpenIddict for Authorization Code + PKCE. The current usable flow is:

1. User registers through `POST /api/users/register`.
2. User verifies email through `POST /api/auth/verify-email`.
3. API establishes the browser sign-in session.
4. SPA completes PKCE through `/connect/authorize` and `/connect/token`.
5. Authenticated SPA calls `GET /api/users/me`.

Anything beyond the verified standalone-registration flow needs a new owning use case before source returns.

## Data Ownership

Identity owns users, personal workspaces, memberships, email-verification tokens, legal acceptance records, and OpenIddict storage. Schema changes use EF Core migrations only.

There are no cross-module data flows in the current repo state.

## Operations

Local development uses Docker Compose for PostgreSQL, Redis, Maildev, optional OpenTelemetry collector, API, frontend, and E2E runner. Health endpoints remain anonymous. Logs must not contain PII.
