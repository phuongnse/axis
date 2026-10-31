# D94. Purchase requests prove the extensibility milestone

Status: **Agreed**

The owner agreed this on 2026-10-25. The end-to-end journey of the
extensibility milestone extends the purchase request sample. It needs no new
sample. [D101](d101-the-extensibility-milestone-is-split-into-six.md) splits
that milestone into M19 to M23, and each one ends with its own step of the
journey. The module with a dependency, the OAuth2 connector and the webhook are
built. The rest is planned, and each point below names the milestone that
builds it. See [extensibility](../product-brief.md#extensibility) in the
product brief.

- **Why this sample:** purchase requests already have a connector, an
  extension package, line items and an approval process, which M19 to M23
  extend.
  The service desk ([D56](d56-a-service-desk-as-the-third-sample-application.md))
  and asset management
  ([D81](d81-asset-management-as-the-fourth-sample-application.md)) are built
  through Studio only. The journey's packages are installed by the
  operator and its modules are not edited in Studio
  ([D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)),
  so a Studio-only journey cannot hold them. A new sample would add a journey
  that proves nothing more. This is the exception to
  [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)'s
  rule that each milestone extends a sample of a new shape: M19 to M23 extend
  the platform's reach into code and operations, not into a new kind of
  application.
- **A module with a dependency** _(built)_: a new sample module,
  `department-budgets`, under `samples/modules/`, depends on the
  `departments` module that leave requests already use. It holds a budget
  entity, a process, a page and a policy, and declares an `approver` role slot. Purchase requests declare it
  and map the slot to their manager role.
- **A custom widget** _(planned for M22)_: a new sample package under
  `samples/packages/` provides a "budget usage" widget
  ([D88](d88-custom-widgets-in-the-designer-palette-and-a-published-sdk.md)).
  The author drags it from the palette onto a purchase request page in Studio.
  This is the only Studio step of the journey.
- **OAuth2 and a structured payload** _(built)_: the purchasing
  system connector signs in with client credentials and sends the approved
  order with its line items as a list
  ([D91](d91-connector-oauth2-and-structured-payloads.md)). The
  purchasing system stub checks the token and the list.
- **A package out of process** _(planned for M21)_: the payment reference
  package runs in the package host ([D90](d90-extension-packages-run-out-of-process.md)).
- **A webhook** _(built)_: an approved request raises an event that
  a webhook delivers, signed ([D92](d92-outbound-webhooks.md)). In the journey
  the receiver first fails, so the delivery becomes a dead letter.
- **The console** _(built)_: the platform operator finds that dead
  letter in the console, redelivers it and sees it delivered
  ([D93](d93-an-operator-web-console.md)).
