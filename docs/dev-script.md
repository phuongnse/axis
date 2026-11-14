# The local stack with scripts/dev.sh

This page says what `scripts/dev.sh` starts, the settings it sets and how it reloads and stops. The other scripts, the Compose
stack and the development data are in [development](development.md).

## Hot reload with `scripts/dev.sh`

`scripts/dev.sh` runs the local stack with hot reload, the way to work on Axis day to day. It needs
Docker, .NET and Node, and works on a fresh clone.

### What it starts

1. It installs the SPA packages when `web/node_modules` is missing or `web/package-lock.json` changed
   since the last install. It installs no Playwright browser.
2. It builds the Keycloak theme into `artifacts/keycloak-theme`, which the Compose `keycloak` service
   mounts, and copies the email theme with it. It fails when one of the theme's files is missing.
3. **Sample themes and licences** _(planned for M33)_. It copies the sample themes of
   `samples/keycloak/themes` beside the Axis theme, and writes the list of
   [open source licences](reference/legal.md#open-source-licences) that `/licences` shows.
4. It starts PostgreSQL, Keycloak, Mailpit and the purchasing system stub with Docker Compose, and
   waits until PostgreSQL accepts connections and Keycloak's `axis` realm, Mailpit and the stub
   answer. The first run builds the stub's image.
5. It installs the TypeScript sample extension package, `samples/packages/payment-terms`, offline, and builds the sample extension package, `samples/packages/PaymentReferences`, into
   `artifacts/extensions`, where the Development settings of the server and the worker point
   `Extensions:Path`. The purchase request sample declares the package, so it does not activate
   without it. The worker runs with `Extensions:Isolation` `inProcess`, so it loads the C# package itself.
6. It installs the built-in modules, `modules/*`, and the sample modules, `samples/modules/*`, into
   `artifacts/modules/<id>/<version>/`, where the Development settings of the server and the worker
   point `Modules:Path`.
7. It installs the built-in applications, `applications/*`, and any sample template under
   `samples/templates/*`, into `artifacts/templates/<id>/<version>/`, and creates that folder even when
   there are none. The Development and E2E settings of the server point `Templates:Path` there. The
   production images, the Compose example and the Helm chart's server set no `Templates:Path`,
   so Studio lists no template there. In a cell the chart sets it on the publisher for the create job,
   see [the publisher in a cell](reference/installation.md#the-publisher-in-a-cell).
8. It builds the agent host, `src/Axis.Agent`, before the server starts, and runs the built `Axis.Agent.dll`
   at once, not under `dotnet watch`. With no `Ai` settings it runs idle, and it serves its health
   endpoints at `http://127.0.0.1:5209`, see [health checks](reference/health-checks.md#agent-host).
9. It builds the Node.js package host, `src/Axis.PackageHost`, and runs the built `Axis.PackageHost.dll host`
   at once, not under `dotnet watch`. The build output holds the Node.js runner beside the dll. The host
   reads `artifacts/extensions` and listens on `127.0.0.1:5310` only, because the runner protocol has no
   authentication. The worker's `Extensions__Runners__Node` names that address, see
   [package runners](reference/extension-packages.md#package-runners).
10. It runs the server and the worker under `dotnet watch` and the SPA on Vite, in one terminal. It
    starts the worker once the server has migrated the tenant databases. The SPA proxies `/api` and
    `/health` to the server.
11. It runs one publisher, `axis-admin publisher`, once the server has migrated the tenant databases.
    The publisher runs from the built `Axis.Admin.dll`, not under `dotnet watch`. The script sets its
    tenant settings as environment variables of that process only, because `Axis.Admin` reads no
    `appsettings` file. They cover `default` and `default-preview`, so it runs preview requests too, see
    [publisher](reference/administration.md#publisher).

Each output line starts with `[server]`, `[worker]`, `[web]`, `[publisher]`, `[agent]` or `[package-host]`, and the script's own
messages start with `[dev]`.

### Settings it sets

- **Studio's new applications.** `scripts/dev.sh` and the Compose server set `Studio:ApplicationsPath`
  to `artifacts/studio-apps`, which `artifacts/` keeps out of git. An exported
  `Studio__ApplicationsPath` replaces that folder. E2E's journey server sets it to a temporary folder.
- **Email.** The worker sends the emails of `notify` steps to Mailpit, as under
  [Docker Compose](development.md#docker-compose). The script sets `Email__Smtp__Host` to `localhost`,
  `Email__Smtp__Port` to Mailpit's SMTP port, `Email__Smtp__UseTls` to `false` and `Email__From` to
  `axis@localhost` for the worker's process only. An exported `Email__*` setting replaces each one, so
  a developer can try a real mail server. The script gives the publisher `Email__*` settings for
  `mailpit:1025`, the address that Keycloak sees, because the publisher writes the realm's mail server
  when it publishes.
- **The preview tenant.** Development, Compose and E2E configure one preview tenant,
  `default-preview`, with `PreviewOf: default`, see
  [preview tenants](architecture.md#preview-tenants). It has its own database `axis_preview` and its
  own roles, `axis_preview_schema` and `axis_preview_runtime`. It has no deployment bindings, so the
  purchase request sample logs `AXC0100` warnings on it, and a connector call there fails. It signs
  people in through the same realm as `default`. The server activates the four samples on it at start,
  and the worker runs it with only its runtime connection string. Its host is `preview.localhost`:
  Compose and `scripts/dev.sh` serve it at `http://preview.localhost:5206`, and `scripts/dev.sh` also
  serves it through the SPA at `http://preview.localhost:5173`. The E2E server and worker serve it on
  `localhost`, while `default` stays on `127.0.0.1`. The realm accepts sign-in and sign-out redirects
  to those addresses.
- **Ports.** `AXIS_POSTGRES_PORT`, `AXIS_KEYCLOAK_PORT`, `AXIS_PURCHASING_PORT`,
  `AXIS_MAILPIT_SMTP_PORT`, `AXIS_MAILPIT_WEB_PORT`, `AXIS_SERVER_PORT`, `AXIS_WEB_PORT`,
  `AXIS_AGENT_HEALTH_PORT` and `AXIS_PACKAGE_HOST_NODE_PORT` move the nine ports, whose defaults are 5432,
  8080, 5207, 1025, 8025, 5206, 5173, 5209 and 5310. When
  `AXIS_POSTGRES_PORT` is set, the script points the server's connection strings and the worker's
  `Tenants__default__ConnectionString` at that port. When `AXIS_KEYCLOAK_PORT` is set, it points the
  server's `Tenants__default__Oidc__Authority` at Keycloak on that port, and its
  `Tenants__default__Bindings__CompanyIssuer__Value` at the `company` realm there. When
  `AXIS_PURCHASING_PORT` is set, it points the server's and the worker's
  `Tenants__default__Bindings__PurchasingSystemUrl__Value`,
  `Tenants__default__Bindings__PurchasingSystemTokenUrl__Value` and
  `Tenants__default__Bindings__PurchaseRequestApprovedWebhookUrl__Value` at the stub on that port.
- **Worker health.** The worker serves `/health/live` and `/health/ready` at `http://127.0.0.1:5208`
  and answers `404` elsewhere. `Health__Urls` moves that address, and two workers on one machine need
  different addresses. An exported `Health__Urls` moves only the worker's address: the script sets the
  agent host's address from `AXIS_AGENT_HEALTH_PORT`.
- **Telemetry.** Neither Docker Compose nor `scripts/dev.sh` starts a telemetry collector, so logs go
  to the console only. Set `OTEL_EXPORTER_OTLP_ENDPOINT` for the server and the worker to export
  traces, metrics and logs, see [telemetry export](reference/host-settings.md#telemetry-export).

### What reloads

- `dotnet watch` applies C# changes, or restarts the server or the worker without a prompt when it
  cannot. Vite applies SPA changes.
- A change to any `*.json` file under an activated application folder restarts the server once, and
  the server activates the edited configuration again. The script watches the `ActivateOnStartup`
  folders itself, and prints a `[dev]` line when it restarts the server. The worker and the SPA keep
  running. If the edited configuration does not activate, the server exits on its own and the script
  stops with a non-zero code.
- A change to the sample extension package's C# code, to the publisher's code, to the agent host's
  code, to the Node.js package host's code, or to a module or a template needs a restart of the script, because the server reads the module and template catalogs
  only when it starts.

### Stopping

Ctrl+C stops the server, the worker, the publisher, the agent host, the Node.js package host and the SPA. PostgreSQL keeps running with its
data, and Keycloak, Mailpit and the purchasing system stub keep running: run `docker compose down` to
stop them. If any of the six processes exits on its own, the others stop too and the script exits
non-zero.
