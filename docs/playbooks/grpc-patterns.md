# gRPC Patterns

> **Navigation**: [← docs/README.md](../README.md) · [← patterns index](./patterns-index.md) · [← AGENTS.md](../../AGENTS.md)

Rules for cross-module synchronous RPC, proto/Buf compatibility, grpcurl verification, and JWT validation in extracted modules.

---

## gRPC sync call (escape hatch)

Used only when a local read model is insufficient. Example: a workflow step needs to verify the *current* user's permissions at execution time (read model could be stale by milliseconds and that matters).

**Step 1 — Define the contract in B's Contracts project:**

```proto
// src/Modules/Identity/Axis.Identity.Contracts/Protos/axis/identity/v1/identity_service.proto
syntax = "proto3";
package axis.identity.v1;

// Workspace scoping: the server MUST derive workspace_id from the caller's
// JWT `workspace_id` claim, never from a request field. Callers forward the inbound
// `authorization` header on every call. See Step 3 below.
service IdentityService {
    rpc GetUserPermissions(GetUserPermissionsRequest) returns (GetUserPermissionsResponse);
}

message GetUserPermissionsRequest {
    string user_id = 1;
}

message GetUserPermissionsResponse {
    repeated string permissions = 1;
}
```


**Buf (repo-wide lint + breaking)** — register every new module proto root before merge:

1. Place files under `src/Modules/{Module}/Axis.{Module}.Contracts/Protos/axis/{module}/v{n}/` with `package axis.{module}.v{n};` matching the directory (`PACKAGE_DIRECTORY_MATCH`).
2. Add the `Protos` directory to `modules:` in [`buf.yaml`](../../buf.yaml) at the repo root (copy an existing line).
3. Keep `Grpc.Tools` `<Protobuf Include=...>` in the `.csproj` — Buf does not replace codegen.
4. Run `buf lint` locally (`buf` CLI) and `python scripts/axis.py check buf-modules` (also runs in CI **Doc drift**).
5. **Changing `buf.yaml` breaking policy or removing a field?** Read [Buf breaking rules](#buf-breaking-rules) first. The v2 category model splits deletion rules in a way that's easy to misread, and "buf passes locally" can mean "no rule fires" rather than "the relaxed rule passed".

CI runs `buf lint` and `buf breaking` against `main` when `.proto` or `buf.yaml` changes.

## Buf breaking rules

Field-deletion enforcement in `buf.yaml` is **non-obvious** because the buf v2 category model splits deletion rules across categories in a way that's easy to misread. Dropping `FIELD_NO_DELETE` alone leaves *zero* enforcement on field removal — the "reserved variant" is not implicit in FILE/PACKAGE.

Rule map (verify with `buf config ls-breaking-rules --version=v2`):

| Rule | Categories | Default | What it does |
|---|---|---|---|
| `FIELD_NO_DELETE` | FILE, PACKAGE | ✓ ON | Fails on **any** field deletion |
| `FIELD_NO_DELETE_UNLESS_NUMBER_RESERVED` | WIRE_JSON, WIRE | ✗ OFF in FILE/PACKAGE | Fails only if deleted field's **number** was not reserved |
| `FIELD_NO_DELETE_UNLESS_NAME_RESERVED` | WIRE_JSON | ✗ OFF in FILE/PACKAGE | Fails only if deleted field's **name** was not reserved |

The current repo policy ([`buf.yaml`](../../buf.yaml)):

```yaml
breaking:
  use:
    - FILE
    - FIELD_NO_DELETE_UNLESS_NUMBER_RESERVED  # explicit add — not in FILE
    - FIELD_NO_DELETE_UNLESS_NAME_RESERVED    # explicit add — not in FILE
  except:
    - FIELD_NO_DELETE                          # drop strict variant
```

Result: a field may be removed **iff** the proto has `reserved <number>;` AND `reserved "<name>";` in the same message. Everything else FILE-strict still applies (no message deletion, no file renames, etc.).

### Verification workflow

When changing `buf.yaml` to allow a previously-forbidden change:

1. **Run `buf config ls-breaking-rules --version=v2`** to see exactly which rules a category contains. Don't infer from category names — `PACKAGE` does *not* relax `FIELD_NO_DELETE`; the relaxed variant lives in `WIRE_JSON`/`WIRE` and is OFF by default.
2. **Test the negative case before declaring the fix works.** Delete a `reserved` line locally, run `buf breaking`, and confirm it now fails. If it passes, you didn't fix it — you disabled enforcement. *"Buf passes" ≠ "rule fires correctly"*. A common bad fix is switching to `use: PACKAGE` or `FILE except FIELD_NO_DELETE` alone; both can pass local buf while enforcing nothing.
3. **Reserved fields alone are hygiene only.** Without an enforced rule, `reserved` is documentation — it tells humans not to reuse the number, but buf won't fail anyone who forgets.

The shortcut "if CI is green, the rule is doing its job" fails here because the absence of an error proves the absence of a check, not the presence of a working check. Always force the rule to fire on a counterexample before trusting it.

**Step 2 — Consuming module gets a generated client via project reference (modulith) or NuGet (extracted):**

```csharp
// WorkflowEngine.Infrastructure — inject the Identity gRPC client
public sealed class PermissionGate(
    IdentityService.IdentityServiceClient identity,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<bool> CanExecuteAsync(Guid userId, string permission, CancellationToken ct)
    {
        Metadata headers = new();
        string? authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
            headers.Add("authorization", authorization);

        GetUserPermissionsResponse resp = await identity.GetUserPermissionsAsync(
            new GetUserPermissionsRequest { UserId = userId.ToString() },
            headers: headers,
            cancellationToken: ct);
        return resp.Permissions.Contains(permission);
    }
}
```

The gRPC channel is configured via `Modules:Identity:GrpcUrl` in `src/Axis.Api/appsettings.json` (see [ADR-016](../TECH_STACK.md#adr-016-service-discovery-via-config-in-modulith-mode-and-k8s-dns-in-production)) — `http://localhost:5280` on the modulith host in development, module service DNS in production when extracted.

**Step 3 — Server side: derive workspace from JWT, not the request payload**

The gRPC server **must** read `workspace_id` from the caller's JWT `workspace_id` claim, never from the request payload. Trusting an `workspace_id` field would let a caller authenticated as workspace A query workspace B's data by passing workspace B's id — every service-to-service hop would then need its own cross-workspace guard. JWT-derived scoping makes the guarantee structural.

```csharp
// src/Modules/DataModeling/Axis.DataModeling.Infrastructure/Grpc/DataModelCatalogGrpcService.cs
internal sealed class DataModelCatalogGrpcService(IDataModelRepository repo)
    : DataModelCatalogService.DataModelCatalogServiceBase
{
    public override async Task<GetModelSummaryResponse> GetModelSummary(
        GetModelSummaryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModelId, out Guid modelId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));

        Guid workspaceId = ResolveCallerworkspaceId(context);

        DataModel? model = await repo.GetByIdAsync(modelId, workspaceId, context.CancellationToken);
        return new GetModelSummaryResponse
        {
            Exists = model is not null,
            ModelName = model?.Name ?? string.Empty,
        };
    }

    private static Guid ResolveCallerworkspaceId(ServerCallContext context)
    {
        Claim? claim = context.GetHttpContext().User.FindFirst("workspace_id");
        if (claim is null || !Guid.TryParse(claim.Value, out Guid workspaceId))
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Caller JWT is missing a valid workspace_id claim."));
        return workspaceId;
    }
}
```

The server is mapped with `.RequireAuthorization()` so JwtBearer middleware runs first; the call reaches this method only when the JWT is valid. Caller-side gRPC clients (`FormModelDeletionGuard`, `FormWorkflowDeletionGuard`) forward the inbound `authorization` header via `Metadata` so the JWT travels with every cross-module call.

## Dev — verify GetUserPermissions with grpcurl

Identity gRPC is mapped on the same Kestrel host as REST (`MapIdentityGrpc()`). The RPC requires a valid JWT (`RequireAuthorization`) and shares the `auth` rate limiter with login endpoints.

Prerequisites:

- [`grpcurl`](https://github.com/fullstorydev/grpcurl) installed locally.
- `Axis.Api` running (e.g. `dotnet run --project src/Axis.Api` — default dev URL `http://localhost:5280`).
- A Bearer access token from an authenticated session (PKCE login via the SPA, or the integration-test helpers in `tests/Api/Axis.Api.Tests/Helpers/AuthHelper.cs`).

```bash
# Optional: list services exposed on the host
grpcurl -plaintext localhost:5280 list

grpcurl -plaintext \
  -H "authorization: Bearer <access_token>" \
  -d '{"user_id":"<user-guid>"}' \
  localhost:5280 axis.identity.v1.IdentityService/GetUserPermissions
```

Replace `<access_token>` and `<user-guid>` with values from your workspace — the server reads the workspace from the JWT's `workspace_id` claim, so no `workspace_id` is sent in the payload. `Unauthenticated` / `PermissionDenied` means the token is missing, expired, or invalid — obtain a fresh token from `POST /connect/token` after PKCE login.

## JWKS-only JWT validation in consuming modules

**Rule:** modules other than Identity validate JWTs **locally via Identity's JWKS endpoint** — never by calling `IdentityDbContext` or any Identity service. Asking Identity "is this user real?" on every request defeats the purpose of stateless JWT and re-introduces the coupling we removed.

Why: Identity issues short-lived JWTs (15 minutes per [sign-in](../use-cases/identity-access/sign-in/)) signed with a key whose public half is published at `/.well-known/jwks.json` (OpenIddict default). Any module that receives a Bearer token can verify the signature, claims (`sub`, `workspace`, `permissions`), and expiry **without a network call to Identity for each request**. JWKS itself is cached locally by `Microsoft.AspNetCore.Authentication.JwtBearer`, so the network cost is once per key-rotation, not once per request.

The escape hatch — when you need *fresh* permission state that the JWT's `permissions` claim cannot give you — is `IdentityService.GetUserPermissions` (gRPC), not a DB lookup.

**Modulith mode (today):** `Axis.Api` is the only host, and OpenIddict validation runs in-process via `.UseLocalServer()` (see `Program.cs`). No JWKS fetch is needed because the issuer is the same process.

**When a module is extracted:** the extracted module wires JWT validation against Identity's JWKS URL. Example for a future standalone `Axis.DataModeling.Service`:

```csharp
// DataModeling.Service Program.cs
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.Authority = configuration["Identity:Authority"];   // e.g. https://identity.axis.internal
        opts.Audience  = "axis_api";
        opts.RequireHttpsMetadata = !env.IsDevelopment();
        // JwtBearer auto-fetches /.well-known/openid-configuration → JWKS URL,
        // caches keys, and rotates on signature failure. No call to Identity per request.
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
```

**Anti-pattern — DB lookup of Identity from another module:**

```csharp
// WRONG — DataModeling reading Identity tables to authenticate
public async Task<bool> IsActiveAsync(Guid userId)
    => await _identityDb.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active);
```

This violates the Share Nothing principle (cross-module DB query), forces every request to round-trip the Identity DB, and breaks the moment Identity is extracted (the project reference disappears). Use the JWT's `sub` + claims; for state newer than the token's TTL (e.g. immediate deactivation), consume `UserDeactivatedEvent` from Kafka and invalidate a local cache — never reach into Identity's DB.
