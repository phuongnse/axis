# D130. "Activate a release" replaces "roll back"

Status: **Proposed**

The owner asked that the docs, the API and the UI use one word for making a release the live one. A release can be
made live again whether it is older than the live one or newer, after an earlier one was made live, so "roll back"
names only half of it. The Studio editors review ([D129](d129-rules-from-the-studio-editors-review.md)) already
gives Studio's button the word Activate…. This decision proposes the same word everywhere else:

- **The concept.** Activating a release makes it the application's live release in the tenant, either way, back to
  an older release or forward to a newer one. The vocabulary in [concepts](../domain/concepts.md) says activate where
  it says roll back.
- **The route.** `POST /api/studio/apps/{application}/releases/{id}/activate` replaces
  `POST .../releases/{id}/rollback`, with the same request, rules and answer, see
  [the authoring API](../reference/authoring.md). The request's kind `rollback` becomes `activate`.
- **The words.** Studio, the console and the docs say activate and activated, never roll back.

Until it is agreed, the docs keep "rollback" for the concept and the route.

_Why:_ one operation with two names makes people ask whether they differ, and a rollback to a newer release reads as
a contradiction.
