# Connectors, events and endpoints

Detailed reference for the resources that connect an application to other systems: connectors
and deployment bindings, events and triggers and inbound endpoints. The outbound webhooks are in
[outbound-webhooks.md](outbound-webhooks.md). How a process calls a
connector is in [process-execution.md](process-execution.md#connector-calls), how it raises an
event is in [process-events.md](process-events.md), and how a caller calls an endpoint is in
[inbound-endpoints.md](inbound-endpoints.md).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Connectors and deployment bindings

This section follows
[D25](../decisions/d25-connectors-deployment-bindings-and-secrets.md).
The `Bindings` block, its startup checks, resolving a binding at run time,
the secret value that hides itself from text, JSON and logs, the `connector`
resource with its [compile checks](configuration.md#configuration-pipeline) and the activation
check are built. So are the bindings that a package operation declares, which
activation checks and the operation context hands out, see
[Capabilities](extension-packages.md#capabilities). Calling a connector and using secrets in its calls are built
too, and so are retries and reconciliation. A `connector` resource
describes an external system that a process can call. Its URL and its secret
are deployment bindings, so a release holds only their names. How a process calls a connector, retries and
reconciles is in [Connector calls](process-execution.md#connector-calls).

### Deployment bindings

- **Binding.** A named setting that a release refers to by name and never
  holds as a value. M5 has two kinds: `url` and `secret`. M15 adds a third,
  `storage`, see **Storage binding** below.
- **No binding resource.** No resource kind declares bindings. A connector
  names one as `{ "binding": "<name>" }`. Where the name is used decides its
  kind: `baseUrl` needs a `url` binding, and `auth` needs a `secret` binding.
  An `oauth2` auth's `tokenUrl` needs a `url` binding, and its `clientId` and
  `clientSecret` need `secret` bindings. A
  [webhook](outbound-webhooks.md#webhook-resource) names a `url` and a `secret` binding.
  A package operation names them in the `capabilities` of its manifest:
  `urls` need `url` bindings and `secrets` need `secret` bindings.
- **Where values live.** Under `Tenancy:Source=configuration`, each tenant holds its values in
  `Tenants:{id}:Bindings:{name}` of the host configuration. Each binding has a `Kind`, `url`, `secret` or `storage`, and a
  `Value`. The server and the worker must both hold them. Under
  `Tenancy:Source=directory`, the server reads the values from the
  [tenant directory](../architecture.md#tenant-directory) instead, and `secret`
  values are stored encrypted and decrypted when resolved. Under `directory`
  the worker reads them from the directory too. The directory holds a `storage`
  binding as well, with its value in the `url` column. It is set with
  [`tenant binding set <id> <name> --storage database`](tenant-setting-commands.md#deployment-bindings),
  and reading the directory fails when its value is not `database`.
- **Secrets from the environment.** A secret's value is set with an
  environment variable, such as
  `Tenants__default__Bindings__PurchasingSystemClientSecret__Value`. A command-line
  argument or a mounted secret file works too. Only in Development may a
  committed `appsettings` file hold one.
- **Secret source check.** Outside Development, startup fails when the
  source that wins for a secret's `Value` is a configuration file, such as
  `appsettings.json`. The message names the tenant, the binding and the file,
  never the value. A file may still hold a placeholder that an environment
  variable overrides, as in the example below.
- **Never shown.** A secret's value never appears in a release, step
  history, audit record, log or problem details.
- **Resolving a secret.** A `secret` binding resolves to a secret value. Its
  text, its JSON and anything logged with it show `[secret]`, never the value
  or a part of it. Code reads the plain value only by asking for it at the
  moment of a call. A secret is never read back from JSON. Asking for a
  `secret` binding as a plain binding fails with a message that names the
  binding.
- **Activation check.** Activating a release for a tenant fails when a
  binding the release uses is missing for that tenant, or is of the wrong
  kind (`AXC0100`). The release uses the bindings of its connectors and its
  webhooks, the
  `capabilities` of each package operation that a process step uses, and the
  `fileStorage` binding of its application manifest, which must be a `storage`
  binding, reported at `/fileStorage/binding`. The
  binding diagnostic names the tenant and the binding.
  Activation checks bindings before it provisions anything, and reports every
  bad binding at once. Startup activation passes in each tenant's bindings,
  so a bad binding stops the start. A tenant is checked only when a release
  is activated for it. Under `directory` the bindings come from the tenant's
  directory entry. On a
  [preview tenant](host-settings.md#preview-tenants), a binding that is missing is `AXC0100`
  with severity warning, not an error. Startup activation logs the warning. Activation goes on, and a call that needs
  the binding fails at run time, where the inspector shows it. A binding of the
  wrong kind (`AXC0100`) and an `http` URL outside Development (`AXC0101`) stay
  errors.
- **Startup check.** A binding whose `Kind` is missing or is not `url`,
  `secret` or `storage`, whose `Value` is blank, a `url` binding whose value is
  not an absolute `http` or `https` URL, or a `storage` binding whose value is
  not `database`, fails startup like the other
  [tenant checks](../architecture.md#tenant-configuration). The message names
  the tenant and the binding, never the value. Under `directory` the same
  check runs when the directory is read, and a `secret` binding must hold a
  stored value, which is not decrypted. If it fails, the server stops its
  start and the worker stays not ready.
- **Resolved at run time.** The worker reads a binding each time it makes a
  call, so changing a secret needs no new release.
- **Storage binding**. The application manifest names
  where the files of its file fields go, as
  `"fileStorage": { "binding": "<name>" }`. That binding has the kind
  `storage`. In M15 its only value is `database`, which stores the bytes in
  the tenant database. Any other value fails the startup check. An
  application without `fileStorage` uses the tenant database too, and its
  activation needs no binding. Activating a release whose `fileStorage`
  binding is missing, or has another kind, fails with `AXC0100`. Other storage
  comes later. Under `Tenancy:Source=directory`, the `fileStorage` binding is a
  `storage` binding of the tenant directory, and its value is `database`. See [the application manifest](resource-files.md#file-storage),
  [file fields](file-fields.md#storage-and-clean-up) and
  [D76](../decisions/d76-file-money-and-enum-label-field-types.md).
- **HTTPS.** A `url` binding may use `http` only in Development. Elsewhere it
  must be `https`. Activation checks each connector's base URL binding, each
  webhook's `url` binding and
  each `url` binding that a package operation step declares (`AXC0101`). The
  call checks it again: a worker outside Development never sends a call to an
  `http` URL, and fails the call instead. A package call is retried instead,
  and the operation does not run.

The purchasing system's bindings for tenant `default` look like this:

```json
"Tenants": {
  "default": {
    "Hosts": [ "localhost" ],
    "ConnectionString": "Host=...",
    "Bindings": {
      "PurchasingSystemUrl": { "Kind": "url", "Value": "https://purchasing.example.test/api" },
      "PurchasingSystemTokenUrl": { "Kind": "url", "Value": "https://auth.purchasing.example.test/oauth/token" },
      "PurchasingSystemClientId": { "Kind": "secret", "Value": "<from environment>" },
      "PurchasingSystemClientSecret": { "Kind": "secret", "Value": "<from environment>" },
      "Files": { "Kind": "storage", "Value": "database" }
    }
  }
}
```

### Connector resource

The purchase request sample holds the purchasing system connector in
`connectors/purchasing-system.json`:

```json
{
  "id": "409bc638-0aec-4084-ada2-e3baed50057d",
  "kind": "connector",
  "name": "PurchasingSystem",
  "formatVersion": 1,
  "baseUrl": { "binding": "PurchasingSystemUrl" },
  "auth": {
    "type": "oauth2",
    "tokenUrl": { "binding": "PurchasingSystemTokenUrl" },
    "clientId": { "binding": "PurchasingSystemClientId" },
    "clientSecret": { "binding": "PurchasingSystemClientSecret" }
  },
  "timeout": "PT30S",
  "idempotency": "Idempotency-Key",
  "operations": [
    {
      "name": "createPurchaseOrder",
      "method": "POST",
      "path": "/purchase-orders",
      "input": [
        { "name": "requestNumber", "type": "text", "required": true },
        { "name": "supplierCode", "type": "text" },
        { "name": "amount", "type": "decimal", "required": true },
        {
          "name": "lines",
          "type": "list",
          "required": true,
          "fields": [
            { "name": "description", "type": "text", "required": true },
            { "name": "quantity", "type": "integer", "required": true },
            { "name": "unitPrice", "type": "decimal", "required": true },
            { "name": "amount", "type": "decimal", "required": true }
          ]
        }
      ],
      "output": [{ "name": "orderNumber", "type": "text", "required": true }],
      "lookup": "findPurchaseOrder"
    },
    {
      "name": "findPurchaseOrder",
      "method": "GET",
      "path": "/purchase-orders/by-key/{idempotencyKey}",
      "input": [],
      "output": [{ "name": "orderNumber", "type": "text", "required": true }]
    }
  ]
}
```

- `baseUrl` names a `url` binding. An operation's `path` is added to it.
- `auth` is optional. Its `type` is one of these:
  - `none`, the same as no `auth`;
  - `bearer`, with `token`, a `secret` binding sent as
    `Authorization: Bearer <value>`;
  - `header`, with `name`, the header name, and `value`, a `secret` binding;
  - `oauth2`, with a token URL and client credentials, see [OAuth2 auth](#oauth2-auth).
- `timeout` is an ISO 8601 duration. It defaults to `PT30S` and is at most
  `PT2M`.
- `idempotency` names the request header that carries the idempotency key.
  It is absent when the system does not support one.
- `operations` lists the named operations.
  - `method` is `GET`, `POST`, `PUT`, `PATCH` or `DELETE`.
  - `path` is a path template. `{idempotencyKey}` holds the operation
    identity. Any other `{name}` placeholder names a `path` input, see
    [Structured payloads](#structured-payloads).
  - `input` fields are sent as a JSON body, unless an input's `in` sends it in
    the path, the query or a header. A `GET` sends no body, so it may have
    `path`, `query` and `header` inputs but no `body` input.
  - `output` fields are read from the JSON response. An output is read from
    the top-level property of its name, unless it has a `source`, a JSON
    Pointer ([RFC 6901](https://www.rfc-editor.org/rfc/rfc6901)) into the
    response body, see [Structured payloads](#structured-payloads). `source` is
    allowed on outputs only.
  - Input and output field types are the
    [rule parameter types](resource-files.md#resource-file-shape): `text`, `integer`,
    `decimal`, `boolean`, `date` and `date-time`. Inputs may also be `object` and
    `list`, see [Structured payloads](#structured-payloads).
- `lookup` is optional and set per operation. It names another operation of
  the same connector that finds an earlier call by its idempotency key.
  - The lookup is a `GET` whose `path` holds `{idempotencyKey}`.
  - It has no inputs, and the same outputs as the operation it serves, each
    with the same `source`.
  - A `200` means found, and its body gives the output. A `404` means not
    found. Anything else is a failed lookup.
- The worker never follows a redirect. A `3xx` response fails the call, so
  the secret never goes to another host.

The sample's `PurchaseRequest` entity has a `purchaseOrderNumber` `text`
field, which the call writes. No policy lets a user update it, so the request
form shows it read-only. The sample activates only for a tenant that holds the
`PurchasingSystemUrl`, `PurchasingSystemTokenUrl`, `PurchasingSystemClientId`
and `PurchasingSystemClientSecret` bindings. The entity also
has a `paymentReference` `text` field, which the sample extension package
writes after the purchase order is created, so the form shows it read-only too.
Development,
Docker Compose, `scripts/dev.sh` and the E2E run point them at the
[purchasing system stub](../architecture.md#purchasing-system-stub).

### OAuth2 auth

A connector can get its access token from an OAuth2 authorization server
through the client credentials grant, as
[D91](../decisions/d91-connector-oauth2-and-structured-payloads.md) agrees.
This auth is built.

```json
"auth": {
  "type": "oauth2",
  "tokenUrl": { "binding": "WarehouseTokenUrl" },
  "clientId": { "binding": "WarehouseClientId" },
  "clientSecret": { "binding": "WarehouseClientSecret" },
  "scope": "orders.write"
}
```

- `tokenUrl` is a `url` binding. `clientId` and `clientSecret` are `secret`
  bindings. `scope` and `audience` are optional strings of 1 to 2000
  characters. The schema refuses `token`, `name` and `value` on an `oauth2`
  auth, and refuses `tokenUrl`, `clientId`, `clientSecret`, `scope` and
  `audience` on the other types.
- **Checks.** Activation checks the three bindings as it checks a
  connector's others. A missing binding, or one of the wrong kind, is
  `AXC0100` at `/auth/tokenUrl/binding`, `/auth/clientId/binding` or
  `/auth/clientSecret/binding`, and a missing one is a warning on a preview
  tenant. An `http` token URL outside Development is `AXC0101`. The compiler
  reports `AXC0310` at the same paths when the application uses the name of
  one of these bindings as another kind elsewhere. The kind of a name is read
  from where it is used, as for an [identity provider](identity-provider.md).
- The worker requests a token with `grant_type=client_credentials`, adding
  `scope` and `audience` when they are set. It sends the client id and secret
  with HTTP Basic authentication, each form-encoded as RFC 6749 section 2.3.1
  says, and sends the token as `Authorization: Bearer <token>`. It does this
  before each call and each lookup.
- The token stays in the worker's memory, for each tenant and connector, until
  a minute before its `expires_in`. It is never stored, logged, traced or
  shown, and each worker replica gets its own.
  - A response without `expires_in`, or with 60 seconds or less, gives a token
    for that one call only.
  - The cache also holds a hash of the token URL, the client id, the client
    secret, the scope and the audience. When one of them changes, the worker
    asks for a new token, so changing a binding needs no restart.
  - Two calls that find the cache empty at the same moment may each request a
    token.
- The token request has the connector's `timeout`, like the operation. The
  call's lease is extended by four times the `timeout` for an `oauth2` connector,
  for a token request and a send, and the same two again after a `401`.
- A test scenario answers the operation from its stub and requests no token.
- A `401` from the system drops the token. The worker gets a new one and
  sends the request once more in the same attempt, with the same idempotency
  key. A second `401` fails the attempt as any `4xx` does. A lookup that gets a
  `401` does the same, so a stale token does not keep a call `unknown`. The
  worker drops the token only when its cache still holds the token that got
  the `401`.
- A failed token request, for any reason, including a redirect, is a retryable attempt, and the
  operation is not sent. A failed lookup stays `unknown`. The attempt row and
  the call record the token endpoint's status code and a fixed error text,
  never its body or an exception message.
- The token URL follows the connector rules: `https` outside Development, and
  no redirects.
- `clientAuth: "privateKeyJwt"`, with the private key in a `secret` binding,
  comes after M16 and reuses the signing code of M16's FAPI 2.0 profile.
  Changing the binding rotates the key without a new release.
- Grants that act for a user, such as the authorization code grant, are not
  offered.

### Structured payloads

Inputs go outside the body, as
[D91](../decisions/d91-connector-oauth2-and-structured-payloads.md) agrees.
Inputs can also nest: declaring an `object` or `list` input is built, and the
`lines` input in the example compiles. A step maps one and sends it as nested
JSON. Bodies stay JSON. An output's `source` is built.

```json
{
  "name": "createShipment",
  "method": "POST",
  "path": "/warehouses/{warehouse}/shipments",
  "input": [
    { "name": "warehouse", "type": "text", "in": "path", "required": true },
    { "name": "dryRun", "type": "boolean", "in": "query" },
    {
      "name": "lines",
      "type": "list",
      "fields": [
        { "name": "sku", "type": "text", "required": true },
        { "name": "quantity", "type": "integer", "required": true }
      ]
    }
  ],
  "output": [
    {
      "name": "trackingNumber",
      "type": "text",
      "source": "/shipment/tracking/number"
    }
  ]
}
```

- **`in`.** `body`, the default, `path`, `query` or `header`. A `header`
  input's name is the header name, so it cannot hold a hyphen yet. Only scalar
  inputs may go outside the body. An optional input with no value is left out.
- **Path placeholders.** Each `{name}` in `path` names a `path` input, and each
  `path` input appears in `path`. Names match ignoring letter case. A `path`
  input is `required`, and none is named `idempotencyKey`, which stays the
  operation identity. Its value is escaped as one path segment, so `a/b c`
  becomes `a%2Fb%20c`. A `query` input becomes a `name=value` pair of the
  query string, escaped the same way.
- **`GET`.** A `GET` may have `path`, `query` and `header` inputs, but no
  `body` input. The JSON body of any other method holds only the `body`
  inputs.
- **`object` and `list`.** _(built)_ An `object` input has nested `fields`. A `list`
  input's items are objects with `fields`. Both need at least one nested field,
  and a scalar input has none. A nested field takes no `in`. Only inputs may be
  `object` or `list`: outputs, module connector slots and package fields stay
  scalar.
  - Nesting goes four levels deep at most, and the input is level 1. An `object`
    or `list` among the `fields` of a level-4 one is `AXC0317`. An `object` or
    `list` goes only in the body, so one with another `in` is `AXC0312`.
  - A nested field name repeats an earlier name of the same `fields`, ignoring
    letter case, is `AXC0096`.
  - A `callConnector` step maps them as in
    [Step shape](process-execution.md#step-shape), and a plain expression for
    one is `AXC0318`. The step stores the nested body when it stages the call,
    as [Staging and the call](process-execution.md#staging-and-the-call) says,
    and the worker sends it as it is.
  - When a step stages its call, an `object` input is stored as a JSON object
    of its nested fields. A `list` input is stored as an array with one object
    for each stored row of the child collection it maps from, in the
    collection's position order, and each item expression is evaluated over
    that row. An empty collection gives `[]`. The worker sends the stored body
    as it is, so a retry sends the same body.
  - A required nested field that gives `null` fails the step with
    `The required input 'lines/1/sku' of the step 'shipLines' gave no value.`
    An expression error in a nested field fails it with
    `The input 'lines/1/quantity' of the step 'shipLines' failed: <kind>.` The
    nested path joins the input names with `/` and uses the item's zero-based
    index for a list, as record error pointers do. No text shows a value, and
    a failed step stages no call.
- **`source`.** _(built)_ An output field may name a JSON Pointer
  ([RFC 6901](https://www.rfc-editor.org/rfc/rfc6901)) into the response body,
  such as `/shipment/tracking/number`. Without it, the output is read from the
  top-level property of its name. Outputs stay scalar, because they write
  subject fields.
  - A token reads an object property by its exact name, and an array element by
    its index, such as `/items/0/sku`. Write `~0` for `~` and `~1` for `/` in a
    token.
  - A `source` must start with `/`, and every `~` must be followed by `0` or
    `1`, otherwise it is `AXC0311`. The empty pointer is refused, because it
    names the whole body, which is never a scalar.
  - When the `source` names nothing in a `2xx` body, a required output leaves
    the call `unknown`, as a missing top-level output does. An optional output
    writes nothing.
  - A lookup's outputs need the same `source` as the operation it serves.
- **Later.** Writing a list from the response into a child collection, and
  bodies that are not JSON.

## Events and triggers

This section follows
[D26](../decisions/d26-events-and-triggers.md). The `event` resource
and its [compile checks](configuration.md#configuration-pipeline) are built: an event file
loads, is checked and is part of the release. A
[raise event step](process-events.md#raise-event-step) raises it. The event
`trigger` resource and its compile checks are built too: an event trigger file
loads, is checked and is part of the release. The worker delivers each raised event to the
`async` event triggers of the active release, see
[Event delivery](process-events.md#event-delivery). A `raiseEvent` step starts the processes of the
`sync` event triggers in its own transaction, see
[Raise event step](process-events.md#raise-event-step). Data change triggers start their
processes in the transaction of a record API create or update, of an inbound endpoint's
create, of a task form save, of an `updateRecord` step, of a `createRecord` step, of a connector output and of a package
operation output, see [Starts from triggers](process-events.md#starts-from-triggers). A delete
trigger starts its process in the transaction of a record API delete, see
[Delete triggers](process-events.md#delete-triggers). The schedule
`trigger` resource and its compile checks are built: a schedule trigger file loads, is checked
and is part of the release. The worker runs schedule triggers, see
[Schedules](process-events.md#schedules).
An `event` resource names something that happened to one record. A
`trigger` resource starts a process for one record when something happens:
an event is raised, a record is created or updated, or a schedule is due. How
a trigger starts a process, and when it skips the start, is in
[Events and triggers](process-events.md).

The examples use the E2E fixture application in `tests/e2e/fixtures/e2e-app`,
with its `Note` entity, its `NoteRows` data source and its `NoteReview` and
`NoteClose` processes. The fixture files do not change until the build issues
add events and triggers.

### Event resource

An event about a note that was closed, in `events/note-closed.json`:

```json
{
  "id": "f86ca48b-4afc-4060-9e40-c423522ea71d",
  "kind": "event",
  "name": "NoteClosed",
  "formatVersion": 1,
  "entity": "Note",
  "fields": [
    { "name": "priority", "type": "integer" },
    { "name": "amount", "type": "decimal" }
  ]
}
```

- `entity` names the entity the event is about. It is not a child entity.
  Each occurrence names one record of it. In a [module](modules-and-templates.md#modules) event, it
  names an entity of the same module without `/`, and the compiler qualifies it
  as `<module id>/<name>`.
- `fields` lists the typed payload fields. Their types are the
  [rule parameter types](resource-files.md#resource-file-shape): `text`, `integer`, `decimal`,
  `boolean`, `date` and `date-time`.
- `fields` is required, but the list may be empty.
- Field names are unique within the event, ignoring letter case.
- An unknown entity is `AXC0110`, a child entity `AXC0111`, a repeated field
  name `AXC0112` and an `enum`, `reference` or `child-collection` field
  `AXC0113`. A type name that is not a field type is `AXC0004`.
- A [raise event step](process-events.md#raise-event-step) raises the event for its
  subject record. Events raised by the platform itself come later.

### Trigger resource

Every trigger file has `"kind": "trigger"`. Its trigger kind goes in `type`:
`event`, `dataChange` or `schedule`. `kind` stays the resource kind, as with
steps, fields and widgets, see [processes](processes.md#resource-shape).
Every trigger names the `process` it starts, and each start is for one record
of the process's subject entity. A schedule trigger without a data source and
a delete trigger start a process without a subject, see
[D70](../decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md).

**Event trigger.** It starts `NoteReview` for each closed note of priority 3
or more, in `triggers/review-closed-notes.json`:

```json
{
  "id": "e348a793-af68-42ce-83b5-336a9b10c7f6",
  "kind": "trigger",
  "name": "ReviewClosedNotes",
  "formatVersion": 1,
  "type": "event",
  "event": "NoteClosed",
  "when": "priority >= 3",
  "mode": "async",
  "process": "NoteReview"
}
```

- `event` names the event. Its entity is the process's subject entity, and
  the start is for the record the occurrence names.
- `when` is optional. It is a boolean expression over the payload fields.
- `mode` is `async` or `sync`, and defaults to `async`.
  - `async`: the worker delivers the occurrence later, and starts the process
    at most once per occurrence and trigger.
  - `sync`: the start commits in the transaction of the step that raised the
    event, so both commit or neither does.

**Data change trigger.** It starts `NoteReview` when a note is created, or
when its `amount` changes, and the amount is 1000 or more, in
`triggers/review-large-notes.json`:

```json
{
  "id": "3cefd292-6c4c-4061-83e6-673993c51421",
  "kind": "trigger",
  "name": "ReviewLargeNotes",
  "formatVersion": 1,
  "type": "dataChange",
  "entity": "Note",
  "on": ["create", "update"],
  "fields": ["amount"],
  "when": "amount >= 1000",
  "process": "NoteReview"
}
```

- `entity` names the entity whose records are watched. It is the process's
  subject entity.
- `on` is a non-empty, unique list of `create`, `update` and `delete`. `delete`
  is listed alone. It starts a process without a subject, because a deleted
  record cannot be a subject. Its `parameters` map each process parameter to an
  expression over the record before the delete, see
  [Delete triggers](process-events.md#delete-triggers). A data change trigger
  that does not list `delete` has no `parameters`.
- `fields` is optional. If present, it is a non-empty, unique list. On an
  update, the trigger fires only when one of the named fields changed. A
  create always passes it.
- `when` is optional. It is a boolean expression over the new record, with
  the same scope as a process's `when`.
- It fires on every committed write through the record command: the record
  API, an inbound endpoint's create, a task form save, an `updateRecord` step,
  a `createRecord` step, a connector output and a package operation output.

**Schedule trigger.** It starts `NoteClose` every night at 02:00 in Ho Chi
Minh City, for each note the `NoteRows` data source returns, in
`triggers/close-notes-nightly.json`:

```json
{
  "id": "26dfa49f-47d1-495d-b021-dabb0d9cbda8",
  "kind": "trigger",
  "name": "CloseNotesNightly",
  "formatVersion": 1,
  "type": "schedule",
  "schedule": "0 2 * * *",
  "timeZone": "Asia/Ho_Chi_Minh",
  "dataSource": "NoteRows",
  "parameters": {},
  "process": "NoteClose"
}
```

- `schedule` is a standard 5-field cron expression: minute, hour, day of the
  month, month and day of the week. Each field is a comma list of `*`,
  numbers, ranges such as `1-5` and steps such as `*/15` or `10-30/5`. A step
  follows `*` or a range, not a single number. The ranges are 0–59, 0–23,
  1–31, 1–12 and 0–7. Day of week 0 and 7 are both Sunday. Month and day names
  such as `JAN` or `MON` are not accepted. When both the day of month and the
  day of week are restricted, a day matches if either one matches, as in
  crontab. A field that starts with `*` counts as unrestricted. A cron that
  never matches a date is invalid: that is the case only when no allowed
  month has an allowed day of month. February counts as 29 days, so
  `0 0 29 2 *` is valid and runs in leap years.
- `timeZone` is required, except in a [module](modules-and-templates.md#processes-pages-policies-and-dependencies),
  where it is not allowed because the schedule runs in the application's time
  zone. It is an IANA time zone id, such as
  `Asia/Ho_Chi_Minh`. It does not settle the application time zone rule of
  [D22](../decisions/d22-current-time-and-time-zones.md).
- `dataSource` names a [data source](data-sources.md) that is not grouped.
  Its root entity is the process's subject entity. Each occurrence starts the
  process for every row it returns, in record id order. The data source's
  `pageSize` and `sort` do not apply. Without `dataSource`, the trigger starts
  a process without a subject once per occurrence, and `parameters` gives the
  process's parameters, see
  [Scheduled jobs](process-events.md#scheduled-jobs).
- `parameters` maps the data source's declared parameters to literal values.
  Without `dataSource`, it maps the process's declared parameters, and `null`
  is never allowed. It may be left out when there are none. A value uses the record API's JSON
  forms: a number for `integer` and `decimal`, `true` or `false` for
  `boolean`, and a string for the other types, which follow the
  [data source parameter](data-sources.md) forms. `null` means the parameter
  is not given, and is allowed for an optional parameter only. Every declared
  parameter needs a key. Keys match the declared names ignoring letter case,
  and the release holds the declared names.

The compiler reports these as compile errors. The event trigger checks are
built, with the codes in the [pipeline](configuration.md#configuration-pipeline): an unknown
`process` is `AXC0129`, an unknown `event` is `AXC0130`, an event about
another entity than the process's subject is `AXC0131`, and a `when` problem
has its expression code at `/when`. The data change trigger checks are built
too: an unknown `entity` is `AXC0143`, an `entity` that is not the process's
subject is `AXC0144`, an unknown or computed name in `fields` is `AXC0145`,
and a `when` problem has its expression code at `/when`. Data change
triggers compile and are part of the release. They fire on every
committed write through the record command. The schedule trigger checks are built too: an invalid `schedule` is
`AXC0160`, an unknown `timeZone` is `AXC0161`, an unknown `dataSource` is
`AXC0162`, a grouped one is `AXC0163`, one over another entity is `AXC0164`,
an unknown or repeated `parameters` key is `AXC0165`, a wrongly typed value
is `AXC0166` and a missing parameter is `AXC0167`. Without a `dataSource`, the
same three codes check the keys and values against the process's parameters,
and a process with a subject is `AXC0249`. Schedule triggers compile
and are part of the release, and the platform computes the next occurrence of
a schedule in its time zone. On a daylight saving change a local time that
does not exist runs at the next valid time, and a repeated local time runs
once, at the first. The worker runs schedule triggers, see
[Schedules](process-events.md#schedules). The schema accepts only `"type": "event"`, `"type": "dataChange"`
and `"type": "schedule"`, so any other `type`, any other `mode`, a `when` or
`mode` on a schedule trigger and the properties of other types are `AXC0004`.

- The `process` exists and has a subject. A trigger that names a process
  without a subject is `AXC0247`, whatever the trigger type. The exceptions are
  a schedule trigger without a data source, which must name a process without
  a subject, otherwise it is `AXC0249`, and a delete trigger, which must too,
  otherwise it is `AXC0256`.
- The subject entity matches. The event's entity, the `entity`, or the data
  source's root entity is the process's subject entity.
- The `event` exists.
- An event trigger's `when` sees only the payload fields and gives a boolean.
- A data change trigger's `when` has the scope of a process expression over
  the new record, and gives a boolean.
- `on` is non-empty and holds only `create`, `update` and `delete`. A delete
  trigger lists `delete` alone, otherwise it is `AXC0254`, and it has no
  `fields`, otherwise it is `AXC0255`.
- Each name in `fields` names a field of the entity that is not computed.
- `mode` is `async` or `sync`.
- `schedule` is a valid cron expression.
- `timeZone` is a known IANA time zone id.
- The `dataSource`, when given, exists and is not grouped.
- Each `parameters` key names a declared parameter of the data source, or of
  the process when there is no `dataSource`, with a literal value of its type.
  On a delete trigger, each key names a parameter of the process, with an
  expression over the deleted record of its type, and every process parameter
  is given.
- Each `type` has only its own properties.

## Inbound endpoints

This section follows
[D27](../decisions/d27-inbound-endpoints.md). The `endpoint` resource,
its compile checks, the call with its `output` and `wait`, its idempotency, and the
status location are built. An `endpoint` resource lets another system create
a record and start a process on it in one call. The call, its responses and its errors are in
[Inbound endpoints](inbound-endpoints.md). How a machine caller
signs in is in [Machine callers](security.md#machine-callers).

### Endpoint resource

The resource and its compile checks are built. The purchase request sample
holds an endpoint that
lets the purchasing system submit a purchase request, in
`endpoints/submit-purchase-request.json`:

```json
{
  "id": "bc241668-fe85-45a4-b7cf-d36da8d56b71",
  "kind": "endpoint",
  "name": "SubmitPurchaseRequest",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "process": "PurchaseRequestApproval",
  "input": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    {
      "name": "department",
      "type": "reference",
      "target": "Department",
      "required": true
    },
    { "name": "supplier", "type": "reference", "target": "Supplier" },
    { "name": "currency", "type": "enum", "values": ["VND", "USD", "EUR"] },
    { "name": "lineItems", "type": "child-collection", "target": "LineItem" }
  ],
  "values": {
    "title": "title",
    "department": "department",
    "supplier": "supplier",
    "currency": "currency",
    "lineItems": "lineItems",
    "status": "'draft'"
  }
}
```

The new request is a `draft` with line items, so it passes the process's
`startCondition`. The process's `submit` step then moves it to `submitted`.
The request's `requester` is its [creator](roles-and-policies.md#record-creator) field, so it gets
the caller's id.

- **Entity and process.** `entity` names the entity of the record the call
  creates. It is not a child entity. `process` names the process the call
  starts on that record, and its subject entity is `entity`. _(planned for
  M19)_ `process` is optional when the endpoint has an `event`. Without it,
  the call creates the record and raises the event, and the event's triggers
  start the work. An endpoint has a `process`, an `event` or both.
  They may name a module entity and process of a module the application
  declares, as `<module id>/<name>`. The call then writes the record into the
  application's copy of the module's table and starts the module process on it,
  see [D103](../decisions/d103-modules-hold-triggers-scenarios-and-connector-slots.md).
  A module still holds no `endpoint` file.
- **Input.** `input` lists the typed input fields of the call's body.
  - Their types are the
    [entity field types](resource-files.md#entity-field-types-and-constraints), including
    `reference` and `child-collection`, with the same constraints, such as
    `required`, `maxLength`, `values` and `target`.
  - `expression`, `sequence` and `creator` are not allowed on an input field.
  - Names are unique within the endpoint, ignoring letter case.
- **Values.** `values` maps subject fields to expressions over the input.
  - Each key names a writable field of `entity`: not computed, and with no
    `sequence` or `creator`.
  - Each value is an [expression](expressions.md) that sees only the input
    fields, and gives the field's type. A literal is a valid expression, such
    as `"'draft'"`.
  - A `child-collection` field takes a `child-collection` input with the same
    target, by its name. The input's rows become the record's rows.
  - Fields that `values` leaves out are `null`, as in a record API create.
- **Event** _(planned for M19)_. `event` is optional. It names an event about
  `entity` and maps each payload field to an expression over the input, as
  [D115](../decisions/d115-an-inbound-endpoint-call-raises-an-event.md) agrees.
  The call raises it for the new record in its transaction, see
  [the call](inbound-endpoints.md).

  ```json
  "event": { "name": "PurchaseRequestReceived", "fields": { "title": "title" } }
  ```

  - The expressions follow the rules of `values`: they see only the input
    fields, and call no rule or aggregate and follow no path.
  - An expression that reads an input that `values` maps to a field marked
    `sensitive` counts as reading that field, so an event that has a
    [webhook](outbound-webhooks.md#webhook-resource) may not carry it.

- **Output.** `output` is optional. It lists field names of `entity` that a
  `201` returns when the instance completes within `wait`. _(planned for M19)_
  An endpoint without a `process` answers `201` as soon as it commits, and has
  no `output` or `wait`.
- **Wait.** `wait` is an ISO 8601 duration, more than zero and at most
  `PT30S`. It is how long the call waits for the instance to complete.
- **Together.** `output` and `wait` are declared together, or neither is.

An endpoint without `output` answers `201` as soon as the record and the
instance are stored. The approval process always waits for a manager, so
`SubmitPurchaseRequest` declares no output.

An endpoint whose process finishes without a human task can return output.
The E2E fixture application in `tests/e2e/fixtures/e2e-app` has a `Note`
entity and a `NoteClose` process that closes a note. An endpoint that creates
a note, closes it and returns its status, in
`endpoints/import-closed-note.json`:

```json
{
  "id": "83244ba2-b26f-4348-aa7a-0e39eb992fd3",
  "kind": "endpoint",
  "name": "ImportClosedNote",
  "formatVersion": 1,
  "entity": "Note",
  "process": "NoteClose",
  "input": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "code", "type": "text", "maxLength": 50 },
    { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 }
  ],
  "values": {
    "title": "title",
    "code": "code",
    "amount": "amount",
    "status": "'open'"
  },
  "output": ["code", "status"],
  "wait": "PT10S"
}
```

When the worker runs the `close` step within 10 seconds, the call answers
`201` with `code` and `status`. Otherwise it answers `202` with a status
location. The purchase request sample holds `SubmitPurchaseRequest`. The E2E
fixture application does not hold `ImportClosedNote`.

A [data change trigger](#trigger-resource) on `create` of the same entity
that starts the same process would start it inside the endpoint's record
create. The endpoint's own start would then find the instance running, and
every call would answer `409`. This is a configuration mistake. The compiler
does not prevent it.

The compiler reports these as compile errors, all in one pass. The
[Resolve step](configuration.md#configuration-pipeline) gives the details.

- The `entity` exists and is not a child entity: `AXC0114` and `AXC0115`.
- The `process` exists, and its subject entity is `entity`: `AXC0116` and
  `AXC0117`. A process without a subject is `AXC0117` too, because the call
  starts the process on the record it creates.
- A name under a module that `modules` does not declare, as `entity` or
  `process`, is `AXC0207` at `/entity` or `/process`, whether the application
  pulls the module in only as another module's dependency or not at all.
- An input field's type is an entity field type, with only that type's
  properties. A property on the wrong type is `AXC0013`, and a missing
  `target` or `values` is `AXC0014`. The schema refuses `label`, `unique`,
  `expression`, `sequence` and `creator` on an input field, as `AXC0004`.
- Input names are unique, ignoring letter case: `AXC0118`.
- A `reference` or `child-collection` input names an existing `target`
  (`AXC0012`). A `reference` target is not a child entity (`AXC0040`). A
  `child-collection` target is a child entity (`AXC0119`).
- Each `values` key names a field of `entity`, once (`AXC0120`). The field is
  not computed, and has no `sequence` or `creator` (`AXC0121`).
- Each `values` expression sees only the input fields, and gives the field's
  type. It cannot call rules or aggregates, or follow a path. An `enum`
  input fits an `enum` field when all its values are the field's values. A
  `reference` input fits a `reference` field with the same target. A
  problem gets its [expression diagnostic](expressions.md#diagnostics) code,
  such as `AXC0048`.
- A `child-collection` field is mapped to a `child-collection` input with the
  same target, by its name: `AXC0122`.
- Each `output` entry names a field of `entity`: `AXC0123`.
- `output` and `wait` are declared together. The schema checks this, so
  breaking it is `AXC0004`.
- `wait` is a valid ISO 8601 duration, more than zero and at most `PT30S`:
  `AXC0124`. So `PT0S` is `AXC0124`.
- _(planned for M19)_ An `event` follows the checks of a
  [raise event step](process-events.md#raise-event-step), with its codes
  reported under `/event`: the event exists (`AXC0125` at `/event/name`), is
  about `entity` (`AXC0126`), and `fields` names each payload field once
  (`AXC0127` at `/event/fields/{key}` and `AXC0128` at `/event/fields`). Each
  expression is checked as a `values` expression, against its payload field's
  type. The schema needs `process`, `event` or both, and refuses `output` and
  `wait` without `process`, so breaking either is `AXC0004`.
