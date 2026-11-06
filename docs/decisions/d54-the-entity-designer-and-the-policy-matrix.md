# D54. The entity designer and the policy matrix

Status: **Agreed**

## Context

A change to an entity's fields can need a [migration](d30-schema-migrations.md) to keep stored data,
and an author who edits JSON by hand learns that one is missing only when activation refuses the
release. Access is spread over [policies](d24-roles-and-policies.md) that may each name several
roles, so no file shows who may do what. And the
[authoring API](d38-studio-edits-application-folders-through-an-authoring-api.md) writes one file per
call, while a field change and its migration are two.

## Decision

- **The entity designer asks for migrations at save.** It edits an entity's fields as a table and
  compares the draft with the entity as Studio loaded it, not with the active release. On save it asks
  about each change to a loaded field that would lose or break stored data, and writes the answers
  into one new `migration` resource, named `<Entity>Change<UTC yyyyMMddHHmmss>` so that an entity's
  migrations sort by time. It never edits an existing migration.
- **An entity and its migration are saved in one call.** A `changes` route checks every create and
  patch of the list under one lock before it writes any, and refuses the whole call when one fails.
- **The policy matrix writes one policy per role.** It shows roles as rows and entities, data sources,
  processes and endpoints as columns, each cell holding the role's actions from all its policies,
  combined as the runtime does. An edit writes only to the role's own `<Role>Access` policy, created
  with that role alone when missing. A grant of any other policy, or of a `<Role>Access` that names
  other roles too, shows as shared and is never rewritten.

The screens are in [Studio](../reference/studio.md) and the
[entity designer](../reference/entity-designer.md), and the routes in
[changes](../reference/authoring.md#changes) and [policy matrix](../reference/authoring-policy-matrix.md).

## Consequences

- The author answers each migration question while the change is fresh, and Validate shows a missing
  migration before a publish.
- A field added and renamed before any publish gets a migration it does not need, because the
  designer does not compare with the tenant. Validate reports it.
- The `changes` route writes several files. A server that dies in the middle can leave the first ones
  written, and the next diagnostics show the mismatch.
- A matrix edit never widens another role's access, but a hand-written policy with several roles
  stays read-only in the matrix, which does not split it.

## Alternatives

- **Save the entity and its migration in two calls.** A failure between them would leave a field
  change without the migration that keeps its data.
- **Edit whichever policy grants the cell.** A policy that names several roles would change their
  access too.
