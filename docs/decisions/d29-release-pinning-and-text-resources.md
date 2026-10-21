# D29. Release pinning and text resources

Status: **Agreed**

Running work stays on its release, including its tasks, forms, texts and
package versions. Roles and policies still come from the active release. See [releases and running work](../reference/processes.md#releases-and-running-work)
for the endpoint and command rules and a purchase request walkthrough.
The owner confirmed these points on issue #412 on 2026-10-15.

- **New work and pinned work:**
  - New work starts on the active release. The record API, data sources,
    process starts, inbound endpoints, triggers, sites and pages use it.
  - Running work stays on its release until it ends. Its steps, connector
    and package calls, tasks, forms and texts all use that release.
  - A data change or event trigger set off by old-release work starts new
    work on the active release.
- **Tasks:**
  - Each task list item and task read returns `releaseId`. The inbox loads
    the texts of each release its rows name. The task page loads the texts
    of the task's release.
  - Step labels, forms, outcomes and field definitions come from the task's
    release. The subject label uses that release's display field.
  - Roles and policies always come from the active release. They decide
    which fields the user may read, including the subject's display field.
    The assignee check stays as it is.
  - A tightened policy applies at once. Removing or renaming a policy can
    lock a user out of an old-release task's fields.
- **Text resources:**
  - Each release carries its own texts. Removing a key in a newer release
    does not change a task still running on an older release.
  - Without `release`, `GET /api/sites/{path}/texts/{locale}` uses the active
    release. With `?release={releaseId}`, it answers for any stored release
    of the site's application. An unknown id, a non-UUID or another
    application's release answers `404`.
  - A key used by a label must exist in every locale of that release, as
    `AXC0027` and `AXC0028` already check. A newer release can remove a key
    its own labels no longer use.
  - Old texts stay readable by release id for as long as the release is
    stored, even after its instances end.
- **Missing pinned package version:**
  - At the start of the server and the worker, every active release and
    every release with a running or waiting instance is rebuilt.
  - A pinned package version that is not installed stops the start with a
    diagnostic naming the tenant, application, release and package version.
  - This start check replaces D28's planned activation check. Its
    diagnostic code is `AXC0146`.

_Why:_ [D11](d11-durable-process-engine.md) and
[execution](../domain/knowledge.md#execution) keep running work on the version
it started with. Pinning texts keeps removed keys readable on old tasks.
[D24](d24-roles-and-policies.md) keeps authorization current, so an old
task cannot preserve a loosened policy. Checking pinned package versions at
start extends [D28](d28-extension-packages.md): a host cannot rebuild
work without the code its release pins.
