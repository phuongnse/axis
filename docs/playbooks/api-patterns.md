# API Patterns

> **Navigation**: [docs/README.md](../README.md) · [AGENTS.md](../../AGENTS.md)

Use `$axis-api-contract` for REST/OpenAPI/API type changes.

## Query & N+1 patterns

Fetch exactly the data the contract needs. Avoid per-row async calls from endpoints or handlers; shape queries/read models in the owning module.

## Response DTO convention

Return stable DTOs from Application/API patterns already used by the module. Do not return anonymous objects or `object` for endpoint bodies.

## Pagination pattern

Paginated list responses should include items plus paging metadata. Keep query parameters stable and documented by OpenAPI.

## Minimal API endpoint wiring

Endpoints stay thin: bind input, dispatch Application request, map `Result` to response/problem details. Require authorization unless explicitly public.

## Request ownership

Request DTOs accept user-authored decisions and required protocol tokens only. Values the server can derive from trusted input or persisted state are server-owned: derive them in the Application/API boundary, exclude them from client request shapes, and return the authoritative value in the response. Client previews of server-owned values are advisory and must not be accepted as the source of truth.

## OpenAPI annotation reference

Annotate routes with names, summaries, tags, success type, and problem responses. Contract changes require generated OpenAPI/frontend API type parity.

## Result → HTTP status code mapping

| Result | HTTP |
|---|---|
| Success | `200`, `201`, or `204` |
| Validation | `400` |
| Unauthorized | `401` |
| Forbidden | `403` |
| Not found / cross-workspace isolation | `404` |
| Conflict | `409` |

## Problem details

Problem responses must include a stable machine-readable `code` extension and a `type` URI derived from that code. Validation problem responses must also include field-level `errorCodes` keyed by JSON field name. Keep `detail` as a human-readable fallback only; frontend behavior and localization map from `code`, `type`, or `errorCodes`, not from parsing `detail` text.

## OpenAPI / Scalar setup

OpenAPI generation is a contract artifact. Regenerate and commit generated files when route/request/response shape changes.
