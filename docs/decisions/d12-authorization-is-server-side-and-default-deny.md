# D12. Authorization is server-side and default-deny

Status: **Agreed**

## Context

Policies grant access to resources, records, fields and actions. A check in the browser can be
skipped, and a rule that fails open exposes every record
([knowledge](../domain/knowledge.md#authorization)).

## Decision

- **No matching grant, or a policy that fails to evaluate, denies access.**
- **The server enforces** every read, write, task action, export and inspection.
- **Client-side conditions only shape the UI.**

The detail is in [authorization](../reference/security.md#authorization).

## Consequences

Every route must call the policy module itself.
