# D3. Technology stack

Status: **Agreed**

- **Backend:** C# on .NET 10 (LTS) with ASP.NET Core.
- **Frontend:** React and TypeScript, Ant Design and ProComponents.
- **Storage:** PostgreSQL. EF Core manages the fixed platform tables. A
  controlled Npgsql layer manages the tables generated for configured
  entities.
- **Tests:** xUnit, Testcontainers (real PostgreSQL) and Playwright.
