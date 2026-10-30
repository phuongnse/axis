# D91. Connector OAuth2 and structured payloads

Status: **Agreed**

The owner agreed this on 2026-10-25. It adds to the `connector` resource of
[D25](d25-connectors-deployment-bindings-and-secrets.md): a new `auth` type,
inputs outside the body, and nested input fields. It replaces D25's line that
`auth` is none, a bearer token or a named header, and the line that inputs are
sent as a JSON body. OAuth2 client credentials are built, and so is an output's `source`. Everything else here is
_(planned for M19)_. See
[connector resource](../reference/integration-resources.md#connector-resource).

- **OAuth2 client credentials:**
  - `auth` gets the type `oauth2`, with `tokenUrl` (a `url` binding),
    `clientId` and `clientSecret` (`secret` bindings), and optional `scope`
    and `audience`.
  - The worker gets an access token through the client credentials grant and
    sends it as `Authorization: Bearer <token>`.
  - The token stays in the worker's memory, for each tenant and connector,
    until a minute before it expires. It is never stored, logged or shown.
  - A `401` from the system drops the token. The worker gets a new one and
    sends the request once more, in the same attempt, with the same
    idempotency key.
  - A failed token request is a retryable attempt. History records the token
    endpoint's status code, never its body.
  - The token URL follows the connector rules: `https` outside Development,
    and no redirects.
  - A second client authentication, `private_key_jwt`, comes after M16, and
    reuses the signing code of M16's FAPI 2.0 profile. Its key is a `secret`
    binding, so rotating it needs no new release.
  - Grants that act for a user, such as the authorization code grant, stay
    out.
- **Where an input goes:** each input field gets `in`: `body` (the default),
  `path`, `query` or `header`. A `{name}` placeholder in `path` names a `path`
  input. Each `path` input appears in the path, and each placeholder names
  one. A `GET` may have `path`, `query` and `header` inputs, but no `body`
  input.
- **Nested inputs:**
  - Input fields get the types `object`, with nested `fields`, and `list`,
    whose items are objects with `fields`. Nesting goes four levels deep at
    most.
  - The step's `input` maps an object with `{ "fields": { … } }`, and a list
    from a child collection of the subject with
    `{ "from": "<child collection>", "fields": { … } }`. Each item expression
    is over the child row.
- **Nested outputs:** an output field gets an optional `source`, a JSON
  Pointer into the response body. Outputs stay scalar, because they write
  subject fields. Writing a list into a child collection comes later.
- **JSON only:** request and response bodies are `application/json`.

_Why:_ many systems accept only OAuth2 tokens, and a token that the worker
gets and refreshes itself keeps a long-lived token out of the bindings. Real
APIs put ids in the path and filters in the query, and an order carries its
lines, so flat body-only inputs could not call them. Keeping outputs scalar
keeps one rule for how a call writes the subject, until writing child rows has
its own design.
