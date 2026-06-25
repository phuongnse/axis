# gRPC Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- patterns index](./patterns-index.md) . [<- AGENTS.md](../../AGENTS.md)

Use `$axis-cross-module-contract` for gRPC/proto changes.

## gRPC sync call (escape hatch)

Use gRPC only when a local read model is insufficient for a synchronous decision. Put contracts in `Axis.{Module}.Contracts/*.proto`.

## Buf breaking rules

Proto compatibility is owned by Buf config and Axis wrappers. Do not bypass them with raw tool commands in docs.

### Verification workflow

When proto layout or contracts change, regenerate/check Buf config, run lint/breaking checks, and use `$axis-ready-review`.

## Dev — verify GetUserPermissions

Manual permission checks are development diagnostics only. Product behavior must still be covered by API/application tests.

## JWKS-only JWT validation in consuming modules

Consuming modules use JWKS to validate JWT signature and expiry, then check workspace and permission claims from the verified token. Do not query Identity tables or call Identity per request just to authenticate.
