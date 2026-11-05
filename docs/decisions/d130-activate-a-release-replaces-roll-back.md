# D130. "Activate a release" replaces "roll back"

Status: **Proposed**

## Context

A release that is not live can be made live again, whether it is older than the live one or newer,
after an earlier one was made live. "Roll back" names only half of that, and a rollback to a newer
release reads as a contradiction. Studio's [Releases](../reference/studio-workspace.md#releases) give
the button the word Activate…, while the [concepts](../domain/concepts.md) and the
[authoring API](../reference/authoring.md) say roll back.

## Decision

This decision proposes one word, activate, for making a release the live one:

- **The concept.** Activating a release makes it the application's live release in the tenant, back
  to an older release or forward to a newer one. The concepts say activate where they say roll back.
- **The route.** `POST /api/studio/apps/{application}/releases/{id}/activate` replaces
  `POST .../releases/{id}/rollback`, with the same request, rules and answer, and the publish
  request's kind `rollback` becomes `activate`.
- **The words.** Studio, the console and the docs say activate and activated, never roll back.

## Consequences

- One operation has one name, so nobody asks whether two names differ.
- The route and the request kind change, so a client of the authoring API changes with them.
- Until this is agreed, the docs keep "rollback" for the concept and the route.
