# Select Site Theme Evidence

> **Navigation**: [docs/use-cases/site-experience/select-site-theme.md](./select-site-theme.md) · [docs/use-cases/site-experience/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `frontend/e2e/select-site-theme.pw.ts` | `python scripts/axis.py frontend script test:e2e -- e2e/select-site-theme.pw.ts` |
| AT-002 | `frontend/e2e/select-site-theme.pw.ts`, `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py frontend script test:e2e -- e2e/select-site-theme.pw.ts`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-003 | `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-004 | `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/UpdateUserThemePreferenceHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Queries/GetCurrentUserProfileHandlerTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
| AT-005, AT-006, AT-008 | `frontend/tests/theme-preferences.test.tsx` | `python scripts/axis.py frontend script test tests/theme-preferences.test.tsx` |
| AT-007 | `frontend/tests/theme-preferences.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/UserLanguagePreferenceEndpointTests.cs` | `python scripts/axis.py frontend script test tests/theme-preferences.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-009 | `frontend/e2e/select-site-theme.pw.ts`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/RegisterUserHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/SignInUserHandlerTests.cs`, `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/VerifyEmailHandlerTests.cs` | `python scripts/axis.py frontend script test:e2e -- e2e/select-site-theme.pw.ts`, `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
