# D56. A service desk as the third sample application

Status: **Agreed**

A service desk is the third sample application, in the thin shape that M11 can
run. The sample folder, the realm users and the `request-approval` template
exist. The Playwright journeys that build it through Studio do not, and are
_(planned for M11)_. See the
[Third application section](../product-brief.md#third-application-service-desk)
of the product brief for the data, flow, security, pages and scenarios. The
owner confirmed these points on issue #783 on 2026-10-20.

- **Application:** a service desk, in `samples/apps/service-desk`. A requester
  reports a problem, the service desk lead triages it, and an agent resolves
  it. Its shape differs from the two request-then-approval samples of
  [D41](d41-leave-requests-as-the-second-sample-application.md).
- **Data:**
  - A ticket has a number from the sequence `SD-{yyyy}-{n:5}`, a title, a
    description, a category, a priority (`low`, `normal`, `high` or
    `urgent`), a requester, an assigned agent, a status and a resolution. The
    title is the display field.
  - A category has a name and a default agent. A few categories are seeded.
- **Process:**
  - A requester submits a ticket.
  - The service desk lead triages it. They set the category and priority and
    assign an agent, or they reject the ticket.
  - The agent resolves it with a resolution.
  - The requester confirms the resolution, which closes the ticket, or
    reopens it.
- **Reopen without a loop:** a reopened ticket ends the process with the
  status `reopened`. The requester submits it again, as a returned purchase
  request is submitted again today. A loop that holds a task comes in M14.
- **Security:**
  - The roles are `requester`, `agent` and `service-desk-lead`.
  - A requester reads and edits only their own tickets. An agent reads the
    tickets assigned to them. The lead reads every ticket.
  - The requester cannot edit the resolution.
  - Everything else is denied by default.
- **Pages:** My tickets, the ticket form, the task inbox and All tickets for
  the lead, in a site at path `service-desk`.
- **Texts:** `en` and `vi`.
- **Tests:** test scenarios cover the happy path, a rejection, a reopen then
  resubmit, and denied access for each role.
- **Users:** two new realm users, `khoa` with the role `agent` and `linh` with
  the role `service-desk-lead`. `anna` also gets the role `requester`. The
  development password stays `axis`. The roles of the existing users stay as
  they are.
- **Built only through Studio:**
  - The committed folder `samples/apps/service-desk` records what the journey
    builds. CI activates it and runs its scenarios, as for the other samples.
  - The main Playwright journey starts from a blank application in Studio. It
    builds the same application with Studio only. It uses no file edit, no
    JSON view and no `axis-admin` command.
  - A second, smaller journey starts from the `request-approval` template.
  - At the end, the main journey compares the folder it built with the
    committed sample. The comparison ignores resource ids and key order. Any
    other difference fails the journey.
  - Then the main journey runs the scenarios from Studio and publishes. Then
    the requester, the service desk lead and the agent take one ticket through
    the process on the site.

_Why:_ the thin shape waits for the timers, loops and notifications of M14
([D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)).
M11 proves authoring, not the service desk shape. Comparing with a committed
folder keeps the sample in CI and proves that Studio writes the same files.
The cost is that a change to the sample needs the journey to follow. Starting
from a blank application proves the most of Studio, and the template journey
stays small. New users leave the roles of the existing users unchanged, so
the other samples and their tests do not change.
