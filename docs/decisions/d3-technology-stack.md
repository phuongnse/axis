# D3. Technology stack

Status: **Agreed**

- **Backend:** C# on .NET 10 (LTS) with ASP.NET Core.
- **Frontend:** React and TypeScript, Ant Design and ProComponents.
  - [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)
    replaces this line: the SPA builds on Ant Design without ProComponents, and
    the UI library stays a detail of the SPA.
- **Storage:** PostgreSQL. EF Core manages the fixed platform tables. A
  controlled Npgsql layer manages the tables generated for configured
  entities.
- **Tests:** xUnit, Testcontainers (real PostgreSQL) and Playwright.
