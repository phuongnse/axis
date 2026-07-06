# Sign In To A Standalone User Account Evidence

> **Navigation**: [docs/use-cases/identity-access/sign-in-user.md](./sign-in-user.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `frontend/e2e/sign-in-user.pw.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/sign-in-user.pw.ts` |
| AT-003, AT-005, AT-007 | `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/SignInUserHandlerTests.cs`, `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-004, AT-008 | `frontend/tests/sign-in-page.test.tsx` | `python scripts/axis.py frontend script test tests/sign-in-page.test.tsx` |
| AT-006 | `frontend/tests/sign-in-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/SignInUserFlowTests.cs` | `python scripts/axis.py frontend script test tests/sign-in-page.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-009 | `frontend/tests/callback-page.test.tsx`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend script test tests/callback-page.test.tsx tests/auth-session-restore.test.ts` |
| AT-010 | `frontend/tests/sign-in-page.test.tsx`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/SignInUserHandlerTests.cs` | `python scripts/axis.py frontend script test tests/sign-in-page.test.tsx`, `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
| AT-011 | `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/SignInUserHandlerTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
| AT-012 | `frontend/tests/sign-in-page.test.tsx`, `frontend/tests/callback-page.test.tsx` | `python scripts/axis.py frontend script test tests/sign-in-page.test.tsx tests/callback-page.test.tsx` |
| AT-013 | `frontend/e2e/sign-in-user.pw.ts`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/sign-in-user.pw.ts`, `python scripts/axis.py frontend script test tests/auth-session-restore.test.ts` |
| AT-014 | `frontend/e2e/sign-in-user.pw.ts`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts -g AT-014`, `python scripts/axis.py frontend script test tests/auth-session-restore.test.ts` |
| AT-015 | `frontend/e2e/sign-in-user.pw.ts`, `frontend/tests/auth-session-restore.test.ts` | `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts -g AT-015`, `python scripts/axis.py frontend script test tests/auth-session-restore.test.ts` |
| AT-016 | `frontend/e2e/sign-in-user.pw.ts`, `frontend/tests/auth-session-restore.test.ts`, `frontend/tests/callback-page.test.tsx` | `python scripts/axis.py local-dev e2e -- e2e/sign-in-user.pw.ts -g AT-016`, `python scripts/axis.py frontend script test tests/auth-session-restore.test.ts tests/callback-page.test.tsx` |
