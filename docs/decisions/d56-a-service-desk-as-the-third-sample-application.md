# D56. A service desk as the third sample application

Status: **Agreed**

## Context

Authoring without JSON needs proof that an author can build a whole application through Studio, and
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) asks each milestone
from then on to extend a sample of a new shape. Purchase requests and leave requests are both a
request then an approval.

## Decision

- **A service desk is the third sample,** in `samples/apps/service-desk`. A requester reports a
  problem, the service desk lead triages it, an agent resolves it, and the requester confirms the
  resolution or reopens the ticket.
- **It has its own roles,** `requester`, `agent` and `service-desk-lead`. The development realm adds
  users for them and leaves the roles of the existing users unchanged.
- **A Playwright journey builds it through Studio only,** from a blank application, with no file
  edit, no JSON view and no `axis-admin` command. It then compares the folder it built with the
  committed sample, runs the scenarios from Studio, publishes, and takes one ticket through the
  process on the site.
- **The committed folder records what the journey builds,** and CI activates it and runs its
  scenarios as it does for the other samples.
- **A second, smaller journey starts from a built-in application** instead of a blank one, see
  [D124](d124-the-organization-and-approvals-modules.md).

Its data, flow, security, pages, scenarios and journeys are in the
[product brief](../product-brief.md#third-application-service-desk).

## Consequences

- Starting from a blank application proves the most of Studio, and the second journey stays small.
- Comparing with a committed folder keeps the sample in CI and proves that Studio writes the same
  files.
- A change to the sample needs the journey to follow.
- New users leave the other samples and their tests unchanged.
