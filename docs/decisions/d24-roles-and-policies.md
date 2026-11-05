# D24. Roles and policies

Status: **Agreed**

## Context

[D12](d12-authorization-is-server-side-and-default-deny.md) makes authorization server-side and
default-deny, and an application must say in its configuration who may do what, on which records
and fields. [Authorization](../domain/knowledge.md#authorization) names the usual failures: record
filters that fail open when combined, checks trusted on the client, and field rules nobody uses.

## Decision

- **Roles and policies are resources.** A `role` declares a role. A `policy` names roles and holds
  grants, each giving actions on one target, such as an entity, a data source or a process. Naming
  an undeclared role is a compile error, and a role a user holds that the application does not
  declare is ignored.
- **Filters and field lists.** An entity grant may have a record `filter`, which may call
  `currentUser()`, and a `fields` list. `currentUser()` is allowed only in policy filters.
- **Default-deny.** No session is `401`, no matching grant is `403`, and a record outside every read
  filter is `404`, because it does not exist for the user.
- **OR, and a mistake never widens access.** Grants combine with OR, and a filter that is false,
  `null` or fails adds no records.
- **Writes check before and after,** so a user cannot move a record out of their reach.
- **Readable fields belong to the entity:** the union of the `fields` of the user's `read` grants,
  whichever grant matched a record.
- **The record creator.** A `text` field may declare `"creator": true` to hold the id of the user
  who created the record, so a policy can grant users their own records.
- **Tasks keep their assignee check,** which is enough to list, read and complete a task.
- **System work is not checked.** The worker, seed data and the record writes of process steps run
  as `system`.
- **Only the server decides.** Page metadata and navigation follow the grants but only shape the UI.
  Every endpoint and engine command asks one module, `Axis.Policy`.

The resources are in [roles and policies](../reference/roles-and-policies.md), and how the server
applies them, every action included, in [authorization](../reference/security.md#authorization).

## Consequences

- One wrong policy cannot expose records that another policy hides.
- Queries stay simple, because the readable fields do not depend on which grant matched a record.
  A field cannot be readable on some records of an entity and hidden on others.
