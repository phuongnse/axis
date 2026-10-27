# D81. Asset management as the fourth sample application

Status: **Agreed**

Asset management is the fourth sample application. It proves M15 the way the
service desk of
[D56](d56-a-service-desk-as-the-third-sample-application.md) proved M11 to
M13, as [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
planned. The owner agreed these points on 2026-10-25. The data, security, pages,
dashboard, and import and export are built in M15. The import, attachment and
maintenance scenarios, and the denied cases they lack, are _(planned for M16)_. See the
[Fourth application section](../product-brief.md#fourth-application-asset-management)
of the product brief for the data, security, pages and scenarios.

- **Application:** asset management, in `samples/apps/asset-management`. An
  asset manager keeps a register of the company's assets, who holds them and
  their maintenance. Its shape is a register with a dashboard, not a request
  and an approval.
- **Data:**
  - An asset has a number from the sequence `AS-{n:6}`, a name, a category, a
    status with labelled values, a location, a holder, a purchase cost in
    money with a currency field, a purchase date and a warranty end date. Its
    attachments are a child collection of files. Its software is a
    `references` field.
  - Maintenance entries are child rows that point to a vendor.
  - Category, location, vendor and software are root entities. Categories and
    locations are seeded.
- **Import and export:** an asset manager imports assets from a CSV file, and
  an auditor exports the asset register.
- **Dashboard:** asset value by location in each currency, assets by status,
  and a KPI of warranties that end within 30 days.
- **Security:** the roles are `asset-manager`, `employee` and `auditor`. An
  asset manager reads and writes everything. An employee reads only the
  assets they hold. An auditor reads and exports everything and writes
  nothing.
- **Warranty reminders:** a scheduled job reminds the asset manager of
  warranties that end soon, with the notifications of
  [D73](d73-in-app-and-email-notifications.md). The process and its weekday
  trigger exist, and the reminder is sent in the site. Its email channel is
  planned for M14.
- **Built through Studio:** a Playwright journey builds the application through
  Studio only, compares its six parts with the committed folder, publishes it,
  imports assets and exports the register on the site, as the service desk
  journey does. The warranty reminder in that journey and running the scenarios
  from Studio are _(planned for M16)_.

_Why:_ the existing samples are two approvals and a service desk. A register
with files, money, many-to-many, import and a dashboard is a shape none of
them has, and it is the shape D46 named for M15. Building it through Studio
only proves that an author can use every new field type without JSON.
