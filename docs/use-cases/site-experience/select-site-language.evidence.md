# Select Site Language Evidence

> **Navigation**: [docs/use-cases/site-experience/select-site-language.md](./select-site-language.md) · [docs/use-cases/site-experience/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/e2e/select-site-language.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/select-site-language.pw.ts` |
| AT-002 | `frontend/e2e/select-site-language.pw.ts`, `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py local-dev e2e -- e2e/select-site-language.pw.ts`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-003 | `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-004 | `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/UpdateUserLanguagePreferenceHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Queries/GetCurrentUserProfileHandlerTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
| AT-005, AT-006 | `frontend/tests/language-preferences.test.tsx` | `python scripts/axis.py frontend script test tests/language-preferences.test.tsx` |
| AT-007 | `frontend/tests/language-preferences.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py frontend script test tests/language-preferences.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-008 | `frontend/e2e/register-user.pw.ts`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/RegisterUserHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/SignInUserHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/VerifyEmailHandlerTests.cs` | `python scripts/axis.py local-dev e2e -- e2e/register-user.pw.ts -g AT-011`, `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
