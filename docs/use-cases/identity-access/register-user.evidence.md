# Register A Standalone User Account Evidence

> **Navigation**: [docs/use-cases/identity-access/register-user.md](./register-user.md) · [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001, AT-002 | `frontend/e2e/register-user.pw.ts` | `python scripts/axis.py local-dev e2e -- e2e/register-user.pw.ts` |
| AT-003 | `tests/Api/Axis.Api.Tests/Identity/RegisterUserFlowTests.cs` | `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-004, AT-005, AT-006 | `frontend/tests/register-page.test.tsx` | `python scripts/axis.py frontend script test tests/register-page.test.tsx` |
| AT-007 | `frontend/tests/verify-email-page.test.tsx`, `frontend/tests/email-confirmation-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/RegisterUserFlowTests.cs` | `python scripts/axis.py frontend script test tests/verify-email-page.test.tsx tests/email-confirmation-page.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` |
| AT-008 | `tests/Modules/Identity/Axis.Identity.Application.Tests/Commands/RegisterUserHandlerTests.cs` | `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Application.Tests/Axis.Identity.Application.Tests.csproj` |
| AT-009 | `frontend/tests/register-page.test.tsx`, `frontend/tests/email-confirmation-page.test.tsx`, `frontend/tests/verify-email-page.test.tsx` | `python scripts/axis.py frontend script test tests/register-page.test.tsx tests/email-confirmation-page.test.tsx tests/verify-email-page.test.tsx` |
| AT-010 | `tests/Modules/Identity/Axis.Identity.Infrastructure.Tests/Services/MailKitEmailSenderTests.cs` | `python scripts/axis.py dotnet test -- --filter FullyQualifiedName~MailKitEmailSenderTests` |
| AT-011 | `frontend/e2e/register-user.pw.ts`, `frontend/tests/register-page.test.tsx`, `tests/Api/Axis.Api.Tests/Identity/RegisterUserFlowTests.cs`, `tests/Modules/Identity/Axis.Identity.Infrastructure.Tests/Services/MailKitEmailSenderTests.cs` | `python scripts/axis.py local-dev e2e -- e2e/register-user.pw.ts -g AT-011`, `python scripts/axis.py frontend script test tests/register-page.test.tsx`, `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`, `python scripts/axis.py dotnet test -- --filter FullyQualifiedName~MailKitEmailSenderTests` |
