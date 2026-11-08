# Outbound connections

This page lists every connection that the hosts of an installation open, with the setting that names
its address, and the rules that keep that list closed. The TLS rule of each connection is under
[TLS](installation.md#tls).

## Connections

The server, the worker and the agent host open these connections:

| Host | Destination | Named by | TLS |
| --- | --- | --- | --- |
| server, worker, agent | The platform database | `ConnectionStrings:Platform`. The agent host opens it only with AI authoring on, see [AI settings](host-settings.md#ai-settings). The server also uses `ConnectionStrings:PlatformSchema`, at start and for the unpooled write connection of `PUT /api/cell/tenants/{id}/quotas` | [PostgreSQL](installation.md#tls) |
| server, worker, agent | Each tenant's database | The [tenant directory](../architecture.md#tenant-directory), with the platform connection's TLS settings | [PostgreSQL](installation.md#tls) |
| server | Each tenant's identity provider: discovery, signing keys and the token endpoint, with DPoP and pushed authorization requests under `fapi2` | The tenant's OIDC `Authority` in the tenant directory | [Identity provider](installation.md#tls) |
| server, worker | The telemetry collector | `OTEL_EXPORTER_OTLP_*`, see [telemetry export](host-settings.md#telemetry-export) | [Telemetry export](installation.md#tls) |
| worker | A connector's service | The `url` binding that the connector names as its `baseUrl`, see [deployment bindings](integration-resources.md#deployment-bindings) | [URL bindings](installation.md#tls) |
| worker | A connector's OAuth2 token endpoint | The `url` binding that an `oauth2` auth names as its `tokenUrl`, see [OAuth2 auth](integration-resources.md#oauth2-auth) | [URL bindings](installation.md#tls) |
| worker | A webhook's receiver | The `url` binding that the webhook names as its `url`, see [outbound webhooks](outbound-webhooks.md#webhook-resource) | [URL bindings](installation.md#tls) |
| worker | A package operation's service | A `url` binding that the operation declares in its `capabilities` | [URL bindings](installation.md#tls) |
| agent | A model provider's API | `Ai:Bindings:<name>:Endpoint`, or the provider's own address when it is empty, see [model bindings](model-bindings.md#bindings) | [Model provider](installation.md#tls) |
| worker | The mail server | `Email:Smtp:Host`, or the tenant's email settings, see [notifications](notifications.md) | [Mail](installation.md#tls) |

The publisher connects to PostgreSQL only, with `ConnectionStrings:PlatformSchema`,
`ConnectionStrings:Platform` for the Studio write lock and `Scenarios:ServerConnectionString`, and
calls no HTTP address. The admin host connects to PostgreSQL
with `Admin:ConnectionString` and `ConnectionStrings:PlatformSchema`. Only `tenant create` of a main
tenant also calls an HTTP address: the identity provider's admin API, at `IdentityProvider:Url`, over
HTTPS outside `Development` and `Testing`. That address is an operator setting, so it is on the
allowlist below.

## Rules

These rules keep every destination in the hands of the operator:

- **The allowlist.** Every address comes from an operator setting or from a deployment binding. The
  operator sets both, and a release holds only binding names, never an address. So the settings and
  the bindings are the allowlist of the server and the worker.
- **No user names an address.** No user, author or API caller can give an address in a request, a
  form, a release or a record. A connector or a package operation can reach only the host of the
  binding it names.
- **No redirects.** A connector call never follows one. A `3xx` fails the call, see
  [call outcomes](process-execution.md#call-outcomes).
- **No network rule in the examples.** The Compose example and the Helm chart set no egress rule. An
  operator may add one that allows only the destinations of this page.
- **Extension packages are trusted code.** A package runs in the worker's process, so installing one
  means trusting its code. The `url` binding limits the client that Axis hands to it, not the
  package's own code.
