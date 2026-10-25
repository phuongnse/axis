# D27. Inbound endpoints

Status: **Agreed**

M5 lets other systems create a record and start a process in one call. The
bearer token checks are built, with the tenant binding, the CSRF exemption
and the development realm's `purchasing-system` client. The endpoint resource,
the `invoke` grant, the call with its `output` and `wait`, its
`Idempotency-Key`, and the status location are built. See
[configuration](../reference/integration-resources.md#endpoint-resource) for the
`endpoint` resource and its policy grant, and
[record API](../reference/inbound-endpoints.md) for the call, its
responses and its errors. The owner confirmed these points on issue #312 on
2026-10-12.

- **Endpoint resource:** an `endpoint` has a name, typed `input` fields, the
  `entity` of the record it creates, `values` that map input to fields, and
  the `process` it starts on that record.
  - It is its own resource kind, not a `trigger`, because it has an input
    schema, an authorization grant and a response.
  - An endpoint always creates its subject record. Acting on an
    existing record comes later.
- **Address:** it answers `POST /api/apps/{app}/endpoints/{name}`.
- **Input types:** input fields use the entity field types, including
  `reference` and `child-collection`. `values` maps each subject field to an
  expression over the input. A child collection takes a child-collection
  input by name. The sample's start condition needs a total amount, which
  needs line items.
- **One transaction:** the call creates the subject record through the
  record create command, so references, sequences, computed fields and
  validations apply. It starts the process in the same transaction, pinned
  to the active release.
- **Bearer tokens only:** callers are systems, not browsers. They send an
  OAuth 2.0 bearer access token from the tenant's identity provider, got
  through client credentials.
- **Token checks:** the server checks the token's signature, issuer,
  audience and expiry against the tenant's `Oidc` authority. The issuer must
  be the tenant's authority, so another tenant's token is a `401`.
- **Audience:** the expected audience is a new tenant setting,
  `Oidc:EndpointAudience`. In the development realm it is `axis-endpoints`,
  added by an audience mapper on the `purchasing-system` client. A separate
  audience keeps endpoint tokens apart from the browser sign-in client.
  - The setting is optional. A tenant without it accepts no bearer token, so
    every endpoint call there is a `401`.
  - The server's startup checks do not change.
- **No cookie session:** endpoints ignore the session cookie, so the CSRF
  header does not apply to them. Bearer tokens are accepted only on endpoint
  routes, and ignored everywhere else.
  - [D86](d86-service-accounts-for-the-record-and-process-apis.md) replaces
    the last sentence: service accounts also send bearer tokens to the record
    API, data source rows and the process API.
- **Roles:** the caller's roles come from the token's roles claim, matched
  as for users.
- **Keycloak:** the development realm gets a confidential client
  `purchasing-system` with a service account. The account holds the `axis`
  client role `purchasing-system`, and a roles mapper writes it to the
  `roles` claim.
- **Grant:** a new policy grant target, `endpoint`, with the action
  `invoke`. It takes no `filter` or `fields`.
- **The grant is enough:** the record create and the process start inside
  the call are part of the endpoint's contract. They need no entity or
  process grant of the caller. The endpoint's `values` limit what it can
  write.
- **Denied:** no token, or a bad one, is `401`. A caller with no `invoke`
  grant is `403`.
- **`400`:** input that does not fit the schema is keyed `/input/<name>`.
  Reference, validation and unique-value errors from the record create are
  keyed `/values/<field>`, since input need not map one-to-one to fields. A
  start condition that fails is keyed `/input`. In every case nothing is
  written.
- **`201`:** the call answers with the record id and the process instance
  id.
- **Output:** an endpoint may declare `output` fields of the subject record
  and a `wait` of at most 30 seconds. The two go together. When the instance
  completes within `wait`, the `201` carries those fields. Otherwise the
  call answers `202` with a status location.
- **Status location:** `GET /api/apps/{app}/endpoints/{name}/instances/{instanceId}`.
  It needs the same bearer token checks and the `invoke` grant. Only the
  caller who made the call can read it. Anyone else, and an unknown
  instance, gets `404`.
- **Idempotency:** a repeat with the same `Idempotency-Key` returns the same
  record and instance, and creates nothing. A key is unique per endpoint and
  caller.
  - A repeat never waits. It follows the instance's state: `201` with output
    when the instance has completed, otherwise `202`.
  - The same key with a different input is `422`, as on the process start
    endpoint.
- **Actor:** the caller's id is the token's `sub` claim. It is the audit
  actor of `record.created` and `process.started`, and both details name the
  endpoint.
- **Creator field:** the new record's creator field gets the caller's `sub`.
- **Display name:** the audit display name comes from the display name
  claim, or from the token's client id (`azp`) when that claim is missing.
  Keycloak service-account tokens have no `name` claim.

_Why:_ see [what applications use most](../domain/knowledge.md#what-applications-use-most)
and [authorization](../domain/knowledge.md#authorization). Other systems need
to hand work to Axis, and an endpoint does that through the same record
command and process start as a user, so no rule is skipped. Bearer tokens
keep machine callers out of the browser session of
[D23](d23-sign-in-through-oidc-and-the-bff-session.md), and checking
them against each tenant's own authority and audience follows
[D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md).
A missing audience, a missing grant or another caller's instance gives no
access, as [D12](d12-authorization-is-server-side-and-default-deny.md)
requires. The `invoke` grant extends the policies of
[D24](d24-roles-and-policies.md), and the endpoint's `values` mapping
limits what a caller can write. A repeat follows the instance's state rather
than a stored response, because storing the response after the wait would
need a second write that a crash can lose.
