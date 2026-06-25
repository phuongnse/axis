# Tech Stack

> **Navigation**: [docs](./README.md) · [AGENTS.md](../AGENTS.md)

This file lists the stack that is actually present in the repository. Removed or future capabilities do not belong here until source and tests return with an approved use case.

## Backend

| Technology | Version | Role |
|---|---|---|
| C# / .NET | 8.x | Runtime and language. |
| ASP.NET Core Minimal API | 8.x | REST gateway and OpenAPI host. |
| MediatR | 12.x | In-module command/query dispatch. |
| FluentValidation | 12.x | Application validation. |
| EF Core | 9.x | Identity persistence and migrations. |
| Npgsql | 9.x | PostgreSQL provider. |
| OpenIddict | 5.x | Authorization Code + PKCE and token storage. |
| BCrypt.Net | 4.x | Password hashing. |
| MailKit | 4.x | SMTP email delivery. |
| StackExchange.Redis | 2.x | Token/session support and local infrastructure dependency. |
| Serilog | 9.x | Structured logging. |
| OpenTelemetry | 1.x | Traces, metrics, and OTLP export. |
| Swashbuckle + Scalar | 6.x / 2.x | OpenAPI document and API reference UI. |

## Frontend

| Technology | Version | Role |
|---|---|---|
| React | 19.x | SPA UI. |
| TypeScript | 6.x | Strict type checking. |
| Vite | 6.x | Dev server and build. |
| TanStack Router | 1.x | File-based SPA routing. |
| TanStack Query | 5.x | Server-state fetching and caching. |
| react-hook-form + Zod | 7.x / 3.x | Registration form state and validation. |
| Zustand | 5.x | Small client-state stores. |
| Tailwind CSS | 3.x | Styling. |
| shadcn / Base UI primitives | current | UI primitive layer. |
| Style Dictionary | 5.x | Design-token generation. |
| zxcvbn-ts | 4.x | Password strength checks. |
| Biome | 2.x | Frontend lint and format. |
| Vitest + Testing Library | 3.x / 16.x | Component tests. |
| Playwright | 1.x | Browser E2E tests. |
| openapi-typescript | 7.x | REST contract to SPA types. |

## Infrastructure

| Technology | Role |
|---|---|
| PostgreSQL 16 | Identity database (`axis_identity`). |
| Redis 7 | Local cache/token-support dependency. |
| Maildev | Local email capture. |
| Docker Compose | Local development stack. |
| Optional OpenTelemetry collector | Local trace/metric/log export target. |

## Architecture Decisions

### ADR-001: Keep Only Implemented Product Slices

**Decision:** A feature stays in source and docs only when it is implemented end to end, actively being implemented, or deliberately being specified as the next concrete slice.

**Reason:** Placeholder modules, workaround-heavy tests, and future-facing docs made the repo look more complete than it was. The current baseline favors a small, honest repository over a broad but partial one.

### ADR-002: Identity Is The Only Current Module

**Decision:** The current modulith contains only Identity. `Axis.Api` composes Identity Application and Infrastructure directly; the frontend calls only `Axis.Api`.

**Reason:** The only retained use case is standalone user registration. Cross-module contracts, brokers, and extra databases would be infrastructure without a usable product path.

### ADR-003: REST/OpenAPI Is The SPA Contract

**Decision:** External browser/API traffic uses REST endpoints documented by OpenAPI. The SPA consumes generated TypeScript types from `openapi.json`.

**Reason:** Generated types keep request/response casing and required fields honest without hand-maintained DTO copies in the frontend.

### ADR-004: OpenIddict For Browser Auth

**Decision:** Use OpenIddict for Authorization Code + PKCE in the SPA flow.

**Reason:** The implemented registration path needs standards-based browser token exchange after email verification. OpenIddict provides the protocol and EF storage without introducing an external identity service.

### ADR-005: EF Core Migrations Only

**Decision:** Schema changes use EF Core migrations. `EnsureCreated` is forbidden in source and tests.

**Reason:** The database shape must be reproducible and reviewable. Migrations are the contract for schema evolution.

### ADR-006: No Extra Runtime Until A Use Case Needs It

**Decision:** Infrastructure that does not serve the retained registration slice is not part of the current stack.

**Reason:** Reintroduce runtime infrastructure only with an owning use case, acceptance criteria, tests, and updated docs.

### ADR-007: React SPA With Generated API Types

**Decision:** Keep React, TanStack Router, TanStack Query, strict TypeScript, design tokens, Vitest, and Playwright as the frontend baseline.

**Reason:** The registration and dashboard slice is user-facing and benefits from a typed route/data stack with behavior-focused tests.
