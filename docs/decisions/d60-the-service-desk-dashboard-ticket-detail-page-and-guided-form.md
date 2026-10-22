# D60. The service desk dashboard, ticket detail page and guided form

Status: **Agreed**

The service desk sample grows new parts in M12. It adds a dashboard, a ticket
detail page, deleting draft tickets and a two-step ticket form. This extends
[D56](d56-a-service-desk-as-the-third-sample-application.md) and does not
replace it. The page list of D56 stays true for M11. See the
[Third application section](../product-brief.md#third-application-service-desk)
of the product brief. All of this is _(planned for M12)_. The owner confirmed
these points on issue #935 on 2026-10-21.

- **Dashboard:** a page for `service-desk-lead`. It shows KPIs (key figures)
  for open and urgent tickets, and a chart of open tickets by category. It
  sits above the All tickets table, in columns.
- **Ticket detail page:** one detail widget that shows the ticket. My tickets
  and All tickets open it with a navigate action and a `ticket` page
  parameter.
- **Delete:** a requester deletes their own ticket while its `status` is
  `draft`. The `agent` and `service-desk-lead` roles cannot delete.
- **Ticket form:** a new ticket is entered in two steps. Step 1 is the title
  and description. Step 2 is the category and priority. The resolution is
  visible, and required, only when the status is `resolved`.
- **Journeys:** the Studio-only journey also builds these parts, so the
  committed sample still equals what Studio builds, and the comparison covers
  them. Another Playwright journey uses the sample at phone width.
- **Scenarios:** a requester deletes a draft ticket, and an agent's delete is
  refused.

_Why:_ M12 adds pages, layouts, widgets, delete and wizard steps
([D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)).
The service desk is the sample that shows them. Extending the Studio-only
journey proves that Studio can build M12 pages. The cost is a longer journey.
Key figures that need SLAs wait for M14. A record history view waits for a
widget that lists the history of a record. Both can be added later through a
newer decision.
