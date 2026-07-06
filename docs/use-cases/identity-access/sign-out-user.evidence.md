# Sign Out Of A Standalone User Account Evidence

> **Navigation**: [docs/use-cases/identity-access/sign-out-user.md](./sign-out-user.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-006 | `frontend/e2e/sign-out-user.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/sign-out-user.pw.ts` |
| AT-002 | `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test` |
| AT-003, AT-004, AT-005 | `frontend/tests/app-shell.test.tsx` | `python scripts/axis.py frontend script test tests/app-shell.test.tsx` |
