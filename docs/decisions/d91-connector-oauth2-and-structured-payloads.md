# D91. Connector OAuth2 and structured payloads

Status: **Agreed**

## Context

A connector of [D25](d25-connectors-deployment-bindings-and-secrets.md) authenticates with a fixed
token or header and sends a flat JSON body. Many systems accept only OAuth2 tokens, real APIs put ids
in the path and filters in the query, and an order carries its lines.

## Decision

- **OAuth2 client credentials.** The worker gets and refreshes the token itself, keeps it only in
  memory, and gets a new one after a `401`. No grant acts for a user.
- **An input goes in the body, the path, the query or a header.**
- **Inputs nest** as objects and lists, and a list maps from a child collection.
- **Outputs stay scalar,** read anywhere in the response by a JSON Pointer.
- **Bodies are JSON.**

The detail is in the [connector resource](../reference/integration-resources.md#connector-resource).

## Consequences

- No long-lived token sits in the bindings.
- A call still writes only subject fields. Writing a response's list into child rows needs a design
  of its own.
- A system that needs a body other than JSON cannot be called through a connector.
