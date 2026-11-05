# D3. Technology stack

Status: **Agreed**

## Context

Every engine, host and screen of Axis, and every agent that builds them, works in one set of
languages, frameworks and test tools. The choice is costly to undo, because the engines, the storage
layer and the SPA all build on it.

## Decision

- **Backend:** C# on .NET 10 (LTS) with ASP.NET Core.
- **Frontend:** React and TypeScript on Ant Design. Ant Design is a detail of the SPA, without
  ProComponents, as [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md) agrees.
- **Storage:** PostgreSQL. EF Core manages the fixed platform tables, and a controlled Npgsql layer
  manages the tables generated for configured entities.
- **Tests:** xUnit, Testcontainers with real PostgreSQL, and Playwright.

## Consequences

- Storage and engine behaviour is tested on real PostgreSQL, so those tests need Docker.
- The generated tables stay out of EF Core, so Axis plans their schema changes itself, see
  [storage](../reference/storage.md#schema-planning).
