# Outbound connections

This page lists every connection that the hosts of an installation open, with the setting that names
its address, and the rules that keep that list closed. The TLS rule of each connection is under
[TLS](installation.md#tls).

## Connections

The hosts of an installation open these connections:

| Host | Destination | Named by | TLS |
| --- | --- | --- | --- |
| server, worker, agent | The platform database | `ConnectionStrings:Platform`. The agent host opens it only with AI authoring on, see [AI settings](host-settings.md#ai-settings). The server also uses `ConnectionStrings:PlatformSchema` at start, and for the unpooled write connection of the provisioning and tenant API writes that need no job | [PostgreSQL](installation.md#tls) |
| server, worker, agent | Each tenant's database | The [tenant directory](../architecture.md#tenant-directory), with the platform connection's TLS settings | [PostgreSQL](installation.md#tls) |
| server | Each tenant's identity provider: discovery, signing keys and the token endpoint, with DPoP and pushed authorization requests under `fapi2` | The tenant's OIDC `Authority` in the tenant directory | [Identity provider](installation.md#tls) |
| server | Each tenant's realm admin API: user administration, the outside clients and consents, and other cell and tenant routes that change the realm | The realm details of the [tenant directory](../architecture.md#tenant-directory) | [Identity provider](installation.md#tls) |
| server | The operator realm, which checks the bearer tokens of the provisioning API | `IdentityProvider:Url` and `Cell:OperatorRealm`, in a cell with a console host or internal URL | [Identity provider](installation.md#tls) |
| server, worker | The telemetry collector | `OTEL_EXPORTER_OTLP_*`, see [telemetry export](host-settings.md#telemetry-export) | [Telemetry export](installation.md#tls) |
| worker | A connector's service | The `url` binding that the connector names as its `baseUrl`, see [deployment bindings](integration-resources.md#deployment-bindings) | [URL bindings](installation.md#tls) |
| worker | A connector's OAuth2 token endpoint | The `url` binding that an `oauth2` auth names as its `tokenUrl`, see [OAuth2 auth](integration-resources.md#oauth2-auth) | [URL bindings](installation.md#tls) |
| worker | A webhook's receiver | The `url` binding that the webhook names as its `url`, see [outbound webhooks](outbound-webhooks.md#webhook-resource) | [URL bindings](installation.md#tls) |
| worker | A package operation's service | A `url` binding that the operation declares in its `capabilities` | [URL bindings](installation.md#tls) |
| agent | A model provider's API | `Ai:Bindings:<name>:Endpoint`, or the provider's own address when it is empty, see [model bindings](model-bindings.md#bindings) | [Model provider](installation.md#tls) |
| worker | The mail server | `Email:Smtp:Host`, or the tenant's email settings, see [notifications](notifications.md) | [Mail](installation.md#tls) |
| worker | A package runner, for each attempt of a package in its language | `Extensions:Runners:Dotnet`, `Extensions:Runners:Node` and `Extensions:Runners:Python`, see [package runners](extension-packages.md#package-runners) | Not encrypted, see [the runner protocol](extension-packages.md#the-runner-protocol) |
| worker, publisher | application-package-host: the worker's runs of an application's packages, and with `Extensions:ApplicationPackages` on, the worker's gVisor check at start and the publisher's package builds and tests | `Extensions:Runners:Application`, see [an application's packages](extension-packages.md#an-applications-packages) | Not encrypted, see [the runner protocol](extension-packages.md#the-runner-protocol) |
| agent | Each tenant's MCP server, at `https://<first tenant host>/mcp`, for the authoring requests | The first host of each tenant in the [tenant directory](../architecture.md#tenant-directory), with AI authoring on, see [the agent host](authoring-requests.md#the-agent-host) | [Incoming traffic](installation.md#incoming-traffic) |
| agent | Each tenant realm's token endpoint, to refresh the author's token as `axis-agent` | The realm details of the [tenant directory](../architecture.md#tenant-directory), with AI authoring on, see [acting for the author](authoring-requests.md#acting-for-the-author) | [Identity provider](installation.md#tls) |
| publisher | The platform database | `ConnectionStrings:PlatformSchema`, `ConnectionStrings:Platform` for the Studio write lock and `Scenarios:ServerConnectionString` | [PostgreSQL](installation.md#tls) |
| publisher | Each tenant realm's admin API, to write the redirect addresses of a host it verifies | The realm details of the [tenant directory](../architecture.md#tenant-directory) | [Identity provider](installation.md#tls) |
| publisher | The `TXT` record `_axis-verify.<host>` of a host with a pending request, to check its token | The name servers of the publisher's system | Plain DNS |
| publisher | The Kubernetes API, to create the certificate of a verified host, in a cell with `Cell:HostCertificateIssuer` set | `Cell:HostCertificateIssuer`, and the cluster's API from the pod's service environment | The cluster's own authority, see [hosts and certificates](cells.md#hosts-and-certificates) |
| admin | The platform database | `Admin:ConnectionString` and `ConnectionStrings:PlatformSchema` | [PostgreSQL](installation.md#tls) |
| admin | The identity provider's admin API, for `tenant create` and `tenant restore`, which make or check a realm, and for `cell operator invite` and `cell operator client add` in a cell, which write the operator realm | `IdentityProvider:Url`, which must be HTTPS outside `Development` and `Testing` | [Identity provider](installation.md#tls) |
| admin | Each tenant realm's admin API, for every command that keeps the realm in step, such as tenant settings, purge, release activation, administrator invite and export | The realm details of the [tenant directory](../architecture.md#tenant-directory) | [Identity provider](installation.md#tls) |

## Rules

These rules keep every destination in the hands of the operator:

- **The allowlist.** Every address comes from an operator setting, from the tenant directory or from a
  deployment binding. Only operator commands and the operator's provisioning API write the directory,
  and a release holds only binding names, never an address. So the settings, the directory and the
  bindings are the allowlist of every host.
- **No user names an address.** No user, author or API caller can give an address in a request, a
  form, a release or a record. A connector or a package operation can reach only the host of the
  binding it names.
- **No redirects.** A connector call never follows one. A `3xx` fails the call, see
  [call outcomes](process-execution.md#call-outcomes).
- **No network rule in the examples.** The Compose example and the Helm chart set no egress rule. An
  operator may add one that allows only the destinations of this page.
- **An installation's packages are trusted code.** Installing one means trusting its code. The `url`
  binding limits the client that Axis hands to it, not the package's own code, see
  [not a sandbox](extension-package-limits.md#not-a-sandbox).
