# Capabilities

What an organization needs from a platform to run its work on it, by area, and where each capability
lives in Axis. A row links the doc section that holds its rule, or carries the planned marker of the
milestone that builds it, as [D154](decisions/d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
agrees. A row that links a decision names something Axis keeps out on purpose, for the reason that
decision gives.

The list follows the references by which such platforms are judged: the workflow patterns of control
flow, resources and data, the executable part of BPMN, the criteria of enterprise application
platform evaluations, and the features of comparable products. `scripts/check-docs.mjs` fails on a row
with no home, so a change that adds a capability, or finds one missing, adds its row in the same
change. What each milestone holds is in [the roadmap](roadmap.md#milestones).

## Processes and tasks

### Paths and flow

| Capability | Where |
| --- | --- |
| Run steps one after another | [Steps](reference/processes.md#steps) |
| Choose one path by condition | [Steps](reference/processes.md#steps) |
| Run several paths at once and wait for all of them | _(planned for M38)_ |
| Choose one or more paths by condition and wait for those taken | _(planned for M38)_ |
| Go on when some of several paths have finished | _(planned for M38)_ |
| Go back to an earlier step through a task or a wait | [Loops](reference/process-flow.md#loops) |
| Repeat steps for each item of a list, one after another | _(planned for M38)_ |
| Repeat steps for each item of a list, all at once | _(planned for M38)_ |
| Run another process and wait until it ends | [Sub-process step](reference/process-flow.md#sub-process-step) |
| Call a process from itself, directly or through others | [Sub-process step](reference/process-flow.md#sub-process-step) |
| Run two cases of one process on the same record at once | [D155](decisions/d155-processes-run-paths-at-once-repeat-steps-give-tasks-to-many-people-and-take-error-paths.md) |
| Run two cases of one job without a subject record at once | [D70](decisions/d70-processes-without-a-subject-scheduled-jobs-and-delete-triggers.md) |
| Run work that concerns no single record, such as a daily job | [Processes without a subject](reference/process-flow.md#processes-without-a-subject) |
| Keep values of a case beyond the fields of its record | _(planned for M38)_ |
| Update the record a case runs for | [Steps](reference/processes.md#steps) |
| Create a record of another entity from a step | [Create record operation](reference/process-flow.md#create-record-operation) |
| Call another system from a step | [Connector calls](reference/process-execution.md#connector-calls) |
| Run custom code from a step | [Package operations](reference/process-execution.md#package-operations) |

### Starts, waits and time

| Capability | Where |
| --- | --- |
| Start a case from a page or through the API | [Start endpoint](reference/processes.md#start-endpoint) |
| Start a case when data changes or an event is raised | [Starts from triggers](reference/process-events.md#starts-from-triggers) |
| Start cases on a schedule | [Schedules](reference/process-events.md#schedules) |
| Start a case from a call by another system | [The call](reference/inbound-endpoints.md#the-call) |
| Wait for a length of time or until a moment | [Timer step](reference/process-flow.md#timer-step) |
| Wait for an event about the record, with a timeout | [Wait for event step](reference/process-flow.md#wait-for-event-step) |
| Keep an event that comes before the case waits for it | [Wait for event step](reference/process-flow.md#wait-for-event-step) |
| Wait for the first of several events | _(planned for M38)_ |
| Match an event to a case by a key other than its record | _(planned for M45)_ |
| Count due dates, deadlines, escalations and timers in working hours, skipping holidays | _(planned for M39)_ |
| Wait for a calendar month or year | _(planned for M39)_ |

### Errors and cancelling

| Capability | Where |
| --- | --- |
| Retry a call to another system on its own, with a wait between tries | [Step shape](reference/process-execution.md#step-shape) |
| Let an operator run a failed step again | [Retry](reference/process-inspection.md#retry) |
| Settle a call whose outcome is unknown | [Resolve](reference/process-inspection.md#resolve) |
| Take another path when a step fails | _(planned for M38)_ |
| Redeliver work that failed too many times | [Dead letters](reference/process-events.md#dead-letters) |
| Cancel a whole case and its sub-processes | [Cancel](reference/process-inspection.md#cancel) |
| Cancel one path or one task while the case goes on | _(planned for M38)_ |
| Undo earlier steps when a case is cancelled | _(planned for M56)_ |
| Suspend a case and resume it later | _(planned for M56)_ |

### Who does a task

| Capability | Where |
| --- | --- |
| Give a task to a named person or one an expression picks | [Steps](reference/processes.md#steps) |
| Give a task to a role, and let one holder claim it | [Claim and release](reference/task-deadlines.md#claim-and-release) |
| Give a task to a line manager or a department head | [The organization module](reference/built-in-modules.md#the-organization-module) |
| Give a task to a list of people | _(planned for M43)_ |
| Give a task to a role within one department | _(planned for M43)_ |
| Ask several people and decide by all, any, a majority or a set number | _(planned for M38)_ |
| Add an approver while a request is in progress | _(planned for M38)_ |
| Give a task to someone else, with a reason | [Reassign](reference/task-deadlines.md#reassign) |
| Share tasks by workload or in turn | _(planned for M43)_ |
| Give an absent approver's approvals to a delegate | [The approval process](reference/built-in-modules.md#the-approval-process) |
| Delegate during an absence in every process, moving tasks already open | _(planned for M43)_ |
| Keep a person from deciding their own request | [The request contract](reference/built-in-modules.md#the-request-contract) |
| Keep one person from deciding two steps of the same case | _(planned for M43)_ |
| Give a step to the person who decided an earlier one | [The accountable user](reference/module-contracts.md#the-accountable-user) |

### Working on tasks

| Capability | Where |
| --- | --- |
| Show a form on a task | [Steps](reference/processes.md#steps) |
| Write a decision comment in a task's form | [Completing a task](reference/task-api.md#completing-a-task) |
| Comments and mentions on a task | _(planned for M43)_ |
| Attach a file to the record a task decides | [Field and value](reference/file-fields.md#field-and-value) |
| Set a due date and show overdue tasks | [Task states](reference/task-deadlines.md#task-states) |
| Remind or escalate a task after a time | [Escalations and deadline](reference/task-deadlines.md#escalations-and-deadline) |
| Move the case on when a task's deadline passes | [Escalations and deadline](reference/task-deadlines.md#escalations-and-deadline) |
| List my open tasks | [Task inbox widget](reference/task-inbox-widget.md#task-inbox-widget) |
| List the tasks I completed | _(planned for M43)_ |
| Notify people in the application or by email from a step | [Notify operation](reference/notifications.md#notify-operation) |
| Tell a person when a task is given to them | _(planned for M43)_ |
| Approve several tasks at once | _(planned for M43)_ |
| Approve from an email | _(planned for M43)_ |
| Work on tasks on a phone screen | [Narrow width](reference/frontend.md#narrow-width) |
| Push notifications to a phone | _(planned for M46)_ |

### Versions, monitoring and design

| Capability | Where |
| --- | --- |
| Keep running cases on the version they started on | [Releases and running work](reference/processes.md#releases-and-running-work) |
| Move running cases to a new version | _(planned for M56)_ |
| See where a case is, what each step did and why it failed | [Instance detail](reference/process-inspection.md#instance-detail) |
| Measure the engine for operations | [Worker metrics](reference/process-execution.md#worker-metrics) |
| Report cycle time, bottlenecks and missed deadlines | _(planned for M54)_ |
| Test a process with scenarios and a moved clock | [Advancing time](reference/test-scenarios.md#advancing-time) |
| Simulate a process under assumed volumes | _(planned for M56)_ |
| Draw and change a process on a canvas | [Canvas](reference/process-designer.md#canvas) |
| Import and export BPMN | _(planned for M56)_ |

## Data, records and reporting

### Fields and relations

| Capability | Where |
| --- | --- |
| Text, whole numbers, decimals with a set precision, yes or no, dates and instants | [Entity field types](reference/resource-files.md#entity-field-types-and-constraints) |
| Amounts in a fixed currency or in the currency of another field | [Entity field types](reference/resource-files.md#entity-field-types-and-constraints) |
| One date for "today" in the application's time zone | [Application time zone](reference/resource-files.md#application-time-zone) |
| A fixed list of values with a label for each | [More field types](reference/resource-files.md#more-field-types-and-properties) |
| A file on a record, and several files through child rows | [File fields](reference/file-fields.md#field-and-value) |
| Multi-line and rich text | _(planned for M42)_ |
| Duration, location, JSON and multi-select fields | _(planned for M53)_ |
| A record that points to another record | [Entity field types](reference/resource-files.md#entity-field-types-and-constraints) |
| Rows owned by a record, such as line items | [Entity logic](reference/resource-files.md#entity-logic) |
| A set of records, many to many | [More field types](reference/resource-files.md#more-field-types-and-properties) |
| A reference found by a unique field instead of being written | [Matched references](reference/resource-files.md#matched-references) |
| A reference that points to one of several kinds of record | _(planned for M53)_ |
| Refuse to delete a record that another record points to | [Create, update and delete](reference/record-api.md#create-update-and-delete) |
| Choose to delete or clear what points to a deleted record | _(planned for M53)_ |
| Tags on a record | [More field types](reference/resource-files.md#more-field-types-and-properties) |

### Rules on data

| Capability | Where |
| --- | --- |
| Values computed from the record and its own rows | [Entity logic](reference/resource-files.md#entity-logic) |
| Values that depend on today's date, such as age or overdue | _(planned for M53)_ |
| Totals over the records that point to this record | _(planned for M53)_ |
| Checks on the record and its own rows | [Entity logic](reference/resource-files.md#entity-logic) |
| Checks that read a referenced record, such as stock on hand | _(planned for M53)_ |
| Unique values, on one field or on several together | [Indexes](reference/resource-files.md#indexes) |
| Indexes for fast lists | [Indexes](reference/resource-files.md#indexes) |
| Business numbers that restart each year | [Sequences](reference/resource-files.md#sequences) |
| Business numbers per month or per prefix | _(planned for M53)_ |
| Default values from a fixed value, a rule or the current time | [More field types](reference/resource-files.md#more-field-types-and-properties) |
| Default values from the user or the page | _(planned for M42)_ |
| Who created a record, for "my records" | [Record creator](reference/roles-and-policies.md#record-creator) |
| Who may read or change which records and which fields | [Policies](reference/roles-and-policies.md#policies) |
| Sensitive values stored encrypted | [Sensitive field values](reference/data-encryption.md#sensitive-field-values) |
| Sensitive values hidden in process inspection | [Values and masking](reference/process-inspection.md#values-and-masking) |
| Part of a value hidden, such as all but the last four digits | _(planned for M47)_ |

### Records over time

| Capability | Where |
| --- | --- |
| Who changed a record, when and which fields | [Audit records and history](reference/record-audit.md#audit-records-and-history) |
| The old and new value of each change | _(planned for M41)_ |
| Search and export of a tenant's audit records | _(planned for M41)_ |
| When a record was last changed and by whom, as fields | _(planned for M41)_ |
| Delete a record and restore it later | _(planned for M41)_ |
| Two people never overwrite each other's changes | [Concurrency](reference/record-api.md#concurrency) |
| Lock a record while one person edits it | _(planned for M53)_ |
| Change many records at once | _(planned for M42)_ |
| Find and merge duplicate records | _(planned for M53)_ |
| Comments and activity on a record | _(planned for M43)_ |
| Preview a file in the page | _(planned for M40)_ |
| Keep earlier versions of a file | _(planned for M40)_ |
| Tell other systems when a record changes | [Outbound webhooks](reference/outbound-webhooks.md#outbound-webhooks) |

### Keeping and removing data

| Capability | Where |
| --- | --- |
| Keep records for a set time, then purge them | _(planned for M47)_ |
| Move old records out of daily lists | _(planned for M47)_ |
| Hold records that a legal case needs | _(planned for M47)_ |
| Export everything about one person | _(planned for M47)_ |
| Erase or anonymise one person | _(planned for M47)_ |
| The operator exports all of a tenant's data | [Exporting a tenant](reference/tenant-lifecycle.md#exporting-a-tenant) |
| A tenant administrator exports the tenant's data | [Exports by a tenant administrator](reference/tenant-lifecycle.md#exports-by-a-tenant-administrator) |
| Daily encrypted backups and restore of a tenant | [Tenant backup](reference/tenant-backup-and-restore.md#tenant-backup) |
| Restore a tenant under a new tenant id | _(planned for M30)_ |
| Keep a tenant's data in one region | [Regions and cells](reference/cells.md#regions-and-cells) |
| Choose the region at sign-up | [The page](reference/sign-up.md#the-page) |

### Import and export

| Capability | Where |
| --- | --- |
| Import records from CSV or Excel with a check before anything is written | [Import](reference/import-export.md#import) |
| Update existing records by a key column in an import | [Upload](reference/import-export.md#upload) |
| Match file columns to fields with other names | _(planned for M53)_ |
| Import child rows and sets of records | _(planned for M53)_ |
| Export a list or a report to CSV or Excel | [Export](reference/import-export.md#export) |
| Export on a schedule | _(planned for M54)_ |
| Load reference data with an application | [Seeds](reference/resource-files.md#seeds) |
| Move live business data between installations | [Out of scope](product-brief.md#out-of-scope) |

### Finding records

| Capability | Where |
| --- | --- |
| Sort, page and filter any list | [Paging and sorting](reference/record-api.md#paging-and-sorting) |
| Filter a report's rows | [Query parameters](reference/data-sources.md#query-parameters) |
| Filter on every column of every table | _(planned for M33)_ |
| Search the text of a list | [Paging and sorting](reference/record-api.md#paging-and-sorting) |
| Search across every kind of record at once | _(planned for M53)_ |
| Replace a saved view with the current search, filter and sort | _(planned for M33)_ |

### Reports and dashboards

| Capability | Where |
| --- | --- |
| Queries over a record and the records it points to | [Resource shape](reference/data-sources.md#resource-shape) |
| Computed columns and joins to records that point here | _(planned for M54)_ |
| Totals, averages and counts by group, by day, week, month or year | [Aggregates](reference/data-sources.md#aggregates) |
| Totals by quarter or fiscal period | _(planned for M39)_ |
| Bar, line and pie charts | [Chart widget](reference/detail-chart-kpi-widgets.md#chart-widget) |
| Stacked and multi-series charts | _(planned for M54)_ |
| Open the records behind a chart | _(planned for M54)_ |
| Key figures with a comparison value | [KPI widget](reference/detail-chart-kpi-widgets.md#kpi-widget) |
| Key figures with a target | _(planned for M54)_ |
| Dashboards with filters | [Data source binding](reference/table-widget.md#data-source-binding) |
| Pivot tables | _(planned for M54)_ |
| Personal dashboards and ad hoc reports | _(planned for M54)_ |
| Reports sent by email on a schedule | _(planned for M54)_ |
| A report or a record as a PDF or a printed page | _(planned for M40)_ |
| Documents made from a template, such as a purchase order | _(planned for M40)_ |
| Read data from a business intelligence tool | _(planned for M54)_ |

### Across the organization

| Capability | Where |
| --- | --- |
| Departments, employees, line managers and delegates | [The organization module](reference/built-in-modules.md#the-organization-module) |
| Several companies in one tenant | _(planned for M53)_ |
| Amounts in several currencies with exchange rates | _(planned for M53)_ |
| Record values in several languages | _(planned for M53)_ |
| Units of measure | _(planned for M53)_ |
| Working days, holidays and fiscal years | _(planned for M39)_ |

## Pages, forms and sites

### Forms

| Capability | Where |
| --- | --- |
| Lay out a form in titled sections | [Form resource](reference/form-widget.md#form-resource) |
| Place fields side by side in columns | [Fields in columns](reference/form-widget.md#fields-in-columns) |
| Put a form's sections in tabs | _(planned for M42)_ |
| Collapse a form section | _(planned for M42)_ |
| Show, require or lock a field by condition | [Conditions](reference/form-conditions.md#conditions) |
| Show validation messages as the person types | [Conditions](reference/form-widget-conditions.md#conditions) |
| Split a form into wizard steps | [Steps](reference/form-conditions.md#steps) |
| Default a field from the context, such as the current person or the page | _(planned for M42)_ |
| Open a new record already filled in from its parent | _(planned for M42)_ |
| Enter line items in a grid inside the form | [Child collections](reference/form-widget.md#child-collections) |
| Reorder line items | _(planned for M42)_ |
| Pick a record through a lookup with search | [Lookup](reference/form-widget.md#lookup) |
| Pick several records | [Set of references](reference/form-widget.md#set-of-references) |
| Narrow a lookup by another field's value | _(planned for M42)_ |
| Pick a person | [Inputs](reference/form-widget.md#inputs) |
| Type text on several lines | _(planned for M42)_ |
| Format text with rich text | _(planned for M42)_ |
| Upload a file | [In the SPA](reference/file-fields.md#in-the-spa) |
| Attach several files to a record | [Field and value](reference/file-fields.md#field-and-value) |
| Preview an image or a PDF without downloading it | _(planned for M40)_ |
| Sign with a signature input | _(planned for M58)_ |
| Take a photo, scan a barcode or record a location in a form | _(planned for M58)_ |
| Keep a form as a draft that saves by itself | _(planned for M42)_ |
| Save and start a process from a form | [Start process actions](reference/form-widget.md#start-process-actions) |
| Show the record's history under its form | [History](reference/form-widget.md#history) |
| Tell the person when someone else changed the record first | [Saving](reference/form-widget.md#saving) |

### Lists and views

| Capability | Where |
| --- | --- |
| Search a table | [Search](reference/table-widget.md#search) |
| Sort and page a table | [Paging and sorting](reference/table-widget.md#paging-and-sorting) |
| Filter a table on its columns | [Filter](reference/table-widget.md#filter) |
| Save a table's search, filter and sort as a named view | [Tables](reference/actions-and-controls.md#tables) |
| Overwrite a saved view | _(planned for M33)_ |
| Keep each person's columns of a table | [Table views and columns](reference/frontend.md#table-views-and-columns) |
| Choose the columns of an application's table | _(planned for M42)_ |
| Act on one row, or from the toolbar | [Row and toolbar actions](reference/table-widget.md#row-and-toolbar-actions) |
| Delete a record from a table | [Delete](reference/table-widget.md#delete) |
| Act on several selected rows at once | _(planned for M42)_ |
| Edit values in the table | _(planned for M42)_ |
| Import records from CSV or Excel into a table | [Import](reference/table-widget.md#import) |
| Show records on a calendar | _(planned for M42)_ |
| Show records on a kanban board | _(planned for M42)_ |
| Show records as cards | _(planned for M42)_ |
| Show records as a tree | _(planned for M42)_ |
| Show records on a Gantt chart or timeline | _(planned for M58)_ |
| Show records on a map | _(planned for M58)_ |
| Show one record read-only | [Detail widget](reference/detail-chart-kpi-widgets.md#detail-widget) |
| Show a record's related lists on its page | _(planned for M42)_ |
| Show a key figure with a comparison | [KPI widget](reference/detail-chart-kpi-widgets.md#kpi-widget) |
| Filter a dashboard and drill down from a chart | _(planned for M42)_ |
| Pivot rows into a cross table | _(planned for M54)_ |
| Build a personal dashboard | _(planned for M54)_ |
| Lay out a page in columns, tabs and sections | [Container widgets](reference/container-widgets.md#the-tree) |
| Show fixed text, images and links on a page | _(planned for M42)_ |
| Open another page with values for its parameters | [Navigate](reference/table-widget.md#navigate) |
| List a person's tasks | [Task inbox widget](reference/task-inbox-widget.md#task-inbox-widget) |
| Decide a task on its own page | [Task page](reference/task-inbox-widget.md#task-page) |
| Follow a process's instances on a page | [Instance list](reference/process-inspector-widget.md#instance-list) |
| Add a widget written as code | _(planned for M22)_ |
| Build pages and forms by drag and drop | [Page designer](reference/studio.md#page-designer) |

### Navigation and personal settings

| Capability | Where |
| --- | --- |
| Show a site's menu with icons | [Navigation icons](reference/shell.md#navigation-icons) |
| Group menu entries under a parent | _(planned for M42)_ |
| Hide pages and widgets a person may not open | [Policies](reference/frontend.md#policies) |
| Open a home page that fits the person's role | _(planned for M42)_ |
| Keep favourites and recent items | _(planned for M42)_ |
| Search across all records of an application | _(planned for M53)_ |
| Move between applications and tools | [App switcher and preferences](reference/shell.md#app-switcher-and-preferences) |
| See every application on one home page | [Platform home page](reference/shell.md#platform-home-page) |
| Choose the language and a light or dark theme | [App switcher and preferences](reference/shell.md#app-switcher-and-preferences) |
| Use every page at phone width | [Narrow width](reference/frontend.md#narrow-width) |
| Read notifications under a bell | [The bell](reference/notifications.md#the-bell) |
| Mark notifications as needing action and clear them | _(planned for M35)_ |
| Choose which notifications arrive and get digests | _(planned for M43)_ |
| See changes by others without reloading | _(planned for M43)_ |
| Comment on and mention people in records and tasks | _(planned for M43)_ |

### Output

| Capability | Where |
| --- | --- |
| Export a table to CSV or Excel | [Export](reference/table-widget.md#export) |
| Send an email from text with placeholders | [Email](reference/notifications.md#email) |
| Make PDF or Word documents from a template | _(planned for M40)_ |
| Print a record in a print layout | _(planned for M40)_ |
| Send SMS, chat and push notifications | _(planned for M46)_ |

### Sites and audiences

| Capability | Where |
| --- | --- |
| Give one application several sites | [Sites and pages](reference/resource-files.md#sites-and-pages) |
| Choose who may sign in to each site | [Sites and sign-in](reference/identity-provider.md#sites-and-sign-in) |
| Serve a site on a domain of its own | [Sites and sign-in](reference/identity-provider.md#sites-and-sign-in) |
| Let a tenant administrator add a domain | [Hosts by a tenant administrator](reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator) |
| Open pages and forms without signing in | _(planned for M49)_ |
| Let outside people register on a portal site | _(planned for M49)_ |
| Embed a page in another site | _(planned for M49)_ |
| Give each site its own logo and colours | _(planned for M42)_ |
| Offer several languages on a site | [Text resolution](reference/frontend.md#text-resolution) |
| Show dates and numbers in locales beyond English and Vietnamese, and right-to-left languages | _(planned for M58)_ |
| Meet contrast, keyboard and screen reader rules | [Design tokens and previews](reference/frontend.md#design-tokens-and-previews) |
| Follow the person's reduced motion setting | [Reduced motion](reference/frontend.md#reduced-motion) |
| State WCAG 2.1 AA conformance, checked in CI | _(planned for M58)_ |
| Install the web app and use it offline | _(planned for M58)_ |
| Add custom HTML, CSS or script to a page | [D8](decisions/d8-custom-ui-widgets-are-packaged-react-components.md) |

## Integration, documents and AI

### Calling other systems

| Capability | Where |
| --- | --- |
| Call another system's REST API with a JSON body | [Connector resource](reference/integration-resources.md#connector-resource) |
| Send values in the path, the query, a header or a nested body | [Structured payloads](reference/integration-resources.md#structured-payloads) |
| Read a value from anywhere in the response | [Structured payloads](reference/integration-resources.md#structured-payloads) |
| Keep each tenant's address and secret of a system out of the release | [Deployment bindings](reference/integration-resources.md#deployment-bindings) |
| Sign in with a bearer token or a secret header | [Connector resource](reference/integration-resources.md#connector-resource) |
| Sign in with OAuth2 client credentials | [OAuth2 auth](reference/integration-resources.md#oauth2-auth) |
| Sign in with basic authentication | _(planned for M45)_ |
| Send an API key in the query | _(planned for M45)_ |
| Sign in on behalf of the user who acts | _(planned for M55)_ |
| Sign in with a client certificate | _(planned for M55)_ |
| Call a SOAP or XML service | _(planned for M55)_ |
| Retry a failed call with backoff | [Call outcomes](reference/process-execution.md#call-outcomes) |
| Act once even when a response is lost | [Reconciliation](reference/process-execution.md#reconciliation) |
| Wait as long as the other system asks before a retry | _(planned for M45)_ |
| Limit how fast calls go to one system | _(planned for M55)_ |
| Turn a response's list into records or child rows | _(planned for M45)_ |
| Read every page of a paged response | _(planned for M45)_ |
| Create a connector from an OpenAPI document | _(planned for M45)_ |
| Answer a call with a stub in a test scenario | [Stubs](reference/test-scenarios.md#stubs) |
| Try a connector call against the real system from Studio | _(planned for M45)_ |
| Run custom code that calls other systems | [Capabilities](reference/extension-packages.md#capabilities) |
| Read and write another database directly | _(planned for M55)_ |
| Send and fetch files over SFTP | _(planned for M55)_ |
| Publish to and read from a message queue | _(planned for M55)_ |
| Show live data from another system on a form | _(planned for M45)_ |

### Other systems calling in

| Capability | Where |
| --- | --- |
| Let another system create a record and start a process in one call | [Endpoint resource](reference/integration-resources.md#endpoint-resource) |
| Let another system announce an event | [Endpoint resource](reference/integration-resources.md#endpoint-resource) |
| Accept a repeated call only once | [Idempotency](reference/inbound-endpoints.md#idempotency) |
| Let the caller read the outcome of a long call later | [Status location](reference/inbound-endpoints.md#status-location) |
| Let a system sign in with its own token | [Machine callers](reference/security.md#machine-callers) |
| Receive a webhook signed with a shared secret | _(planned for M45)_ |
| Update an existing record from a call | _(planned for M45)_ |
| Wake a waiting process from another system's callback | _(planned for M45)_ |
| Turn an incoming email and its attachments into a record | _(planned for M45)_ |
| Pick up files dropped in a folder | _(planned for M55)_ |

### Events and automation

| Capability | Where |
| --- | --- |
| Start a process when a record is created or changed | [Data change trigger](reference/integration-resources.md#data-change-trigger) |
| Start a process when a record is deleted | [Delete triggers](reference/process-events.md#delete-triggers) |
| Start a process on a schedule for each matching record | [Schedule trigger](reference/integration-resources.md#schedule-trigger) |
| Run a scheduled job that has no record | [Scheduled jobs](reference/process-events.md#scheduled-jobs) |
| Raise a business event and react to it | [Raise event step](reference/process-events.md#raise-event-step) |
| Tell another system about an event through a signed webhook | [Webhook delivery](reference/outbound-webhooks.md#webhook-delivery) |
| Let another system subscribe itself to events | [D92](decisions/d92-outbound-webhooks.md) |
| Keep failed deliveries as dead letters | [Dead letters](reference/process-events.md#dead-letters) |
| Redeliver or discard a dead letter | [Commands](reference/operator-console.md#commands) |
| Send past events again | _(planned for M45)_ |

### Messages to people

| Capability | Where |
| --- | --- |
| Notify people in the application | [In-app notifications](reference/notifications.md#in-app-notifications) |
| Email people from a process | [Email](reference/notifications.md#email) |
| Fill message texts from the record in each person's language | [Placeholders](reference/notifications.md#placeholders) |
| Send a tenant's email through its own mail server | [Mail settings](reference/notifications.md#mail-settings) |
| Send email with formatting and attachments | _(planned for M40)_ |
| Email everyone who holds a role | _(planned for M46)_ |
| Send a text message | _(planned for M46)_ |
| Send a Zalo message | _(planned for M46)_ |
| Send a WhatsApp message | _(planned for M46)_ |
| Post to a Microsoft Teams or Slack channel | _(planned for M46)_ |
| Send a push notification | _(planned for M46)_ |
| See whether a message was delivered | _(planned for M46)_ |

### Files and documents

| Capability | Where |
| --- | --- |
| Upload and download files under the record's policies | [Download](reference/file-fields.md#download) |
| Refuse executables and files whose content does not match their type | [Field and value](reference/file-fields.md#field-and-value) |
| Scan uploads for malware | [D76](decisions/d76-file-money-and-enum-label-field-types.md) |
| Keep files outside the database | _(planned for M40)_ |
| Pass a file to a connector, a package or an email | _(planned for M40)_ |
| Have a document signed electronically | _(planned for M40)_ |
| Read values out of a scanned or uploaded document | _(planned for M48)_ |

### Data for other systems

| Capability | Where |
| --- | --- |
| Read and write records through an API | [Record API](reference/record-api.md#routes) |
| Read a data source's rows through an API | [Endpoint](reference/data-sources.md#endpoint) |
| Let a system act under policies as a service account | [Service accounts](reference/security.md#service-accounts) |
| Limit how often a caller may call | [Rate limits](reference/headers-and-rate-limits.md#rate-limits) |
| A tenant administrator creates API clients and service accounts | _(planned for M44)_ |
| A tenant administrator sets connections and their secrets | _(planned for M44)_ |
| Create or update by a key in one import | [Rows and references](reference/import-export.md#rows-and-references) |
| Create or update by a key through the API | _(planned for M45)_ |
| Write many records in one API call | _(planned for M45)_ |
| Read only the records changed since a time | _(planned for M45)_ |
| Publish an OpenAPI document of an application's API | _(planned for M45)_ |
| Query records through OData or GraphQL | _(planned for M54)_ |
| Client libraries for an application's API | _(planned for M45)_ |
| Sync people and groups from a company directory | _(planned for M52)_ |

### Custom code

| Capability | Where |
| --- | --- |
| Add operations written in C#, TypeScript or Python | [What a package is](reference/extension-packages.md#what-a-package-is) |
| Run package code in runners that reach only declared hosts | [Package runners](reference/extension-packages.md#package-runners) |
| A tenant's author writes packages that run in a sandbox | _(planned for M37)_ |
| Run a package in the customer's own network | _(planned for M37)_ |
| Call custom code from an expression | _(planned for M45)_ |
| Add custom widgets to pages | _(planned for M22)_ |

### AI

| Capability | Where |
| --- | --- |
| An agent changes an application through an MCP server | [Tools](reference/mcp-server.md#tools) |
| Describe a change in words in Studio and review its plan and diff | _(planned for M35)_ |
| An agent builds a whole application or module | _(planned for M36)_ |
| Choose the model the agent uses | [Bindings](reference/model-bindings.md#bindings) |
| Use models through Amazon Bedrock or Google Vertex AI | _(planned for M35)_ |
| Each author pays for their requests with their own key | _(planned for M35)_ |
| Keep records, files and secrets away from the model | [What reaches the model](reference/authoring-requests.md#what-reaches-the-model) |
| Bound each request's tokens and fix rounds | [Budgets](reference/authoring-requests.md#budgets) |
| Audit each AI request | [Audit](reference/authoring-requests.md#audit) |
| Classify, summarize or extract in a process step | _(planned for M48)_ |
| Chat over an application's data | _(planned for M48)_ |
| Agents act on records and tasks under policies | _(planned for M48)_ |

### Ready-made connections

| Capability | Where |
| --- | --- |
| Sign in with Microsoft or Google accounts | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Connectors for Microsoft 365 and Google Workspace | _(planned for M50)_ |
| Connectors for Vietnamese e-invoices | _(planned for M50)_ |
| Connectors for banks, payments and VietQR | _(planned for M50)_ |
| Connectors for ERP and CRM systems | _(planned for M50)_ |
| A marketplace of modules and templates | _(planned for M50)_ |

## Security, change and operations

### Sign-in and identity

| Capability | Where |
| --- | --- |
| Sign in through the organization's provider over OpenID Connect | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Sign in through a SAML 2.0 provider | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Sign in with work accounts from a cloud directory or a consumer account provider | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Accounts with a password and a code from an authenticator app | [Accounts](reference/user-administration.md#accounts) |
| Require a level of assurance from a provider | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Sign in with a passkey | _(planned for M52)_ |
| A different way to sign in for each site, such as staff and citizens | [Sites and sign-in](reference/identity-provider.md#sites-and-sign-in) |
| Sign in to Studio and the consoles through the organization's provider | [Platform sign-in](reference/identity-provider.md#platform-sign-in) |
| Invite people from outside the organization | [Inviting and resending](reference/user-administration.md#inviting-and-resending) |
| Register on a public portal without an invitation | _(planned for M49)_ |
| Session lifetime, idle timeout and signing in again | [Session lifetimes and re-authentication](reference/security.md#session-lifetimes-and-re-authentication) |
| See and end one's own sessions | [Sessions](reference/platform-pages.md#sessions) |
| End the session when the person signs out at their provider | _(planned for M52)_ |
| Allow sign-in only from known networks or devices | _(planned for M52)_ |
| Stop a person's access at once | [Disabled users](reference/user-administration.md#disabled-users) |
| Create and remove users from the organization's directory | _(planned for M52)_ |
| Give roles from directory groups at sign-in | [Identity provider resource](reference/identity-provider.md#identity-provider-resource) |
| Keep directory groups in step before people sign in | _(planned for M52)_ |
| Groups of people that hold roles | _(planned for M52)_ |
| Accounts for other systems that call the APIs | [Service accounts](reference/security.md#service-accounts) |
| A sign-in profile for banks and government | [Tenant setting](reference/fapi2-profile.md#tenant-setting) |
| Rate limits on sign-in and the APIs | [Rate limits](reference/headers-and-rate-limits.md#rate-limits) |
| Security headers on every answer | [Security headers](reference/headers-and-rate-limits.md#security-headers) |

### Access control

| Capability | Where |
| --- | --- |
| Roles, with everything denied that no grant allows | [Policies](reference/roles-and-policies.md#policies) |
| Access to records by owner, team or state | [Policies](reference/roles-and-policies.md#policies) |
| Access to single fields | [Policies](reference/roles-and-policies.md#policies) |
| Access along the org hierarchy, up to three levels | [Policies](reference/roles-and-policies.md#policies) |
| Access along the org hierarchy at any depth | _(planned for M52)_ |
| Share one record with a person or a team | _(planned for M52)_ |
| Approvals go to a delegate while the approver is away | [The approval process](reference/built-in-modules.md#the-approval-process) |
| Nobody decides their own request | [The accountable user](reference/module-contracts.md#the-accountable-user) |
| Platform roles for authors, publishers, operators and administrators | [Roles](reference/user-administration.md#roles) |
| Author, publisher and administrator roles for one application only | _(planned for M44)_ |
| An administrator for one department's people | _(planned for M44)_ |
| See what each role may do | [Policy matrix](reference/studio.md#policy-matrix) |
| See who can reach a record and what a person can reach | _(planned for M52)_ |
| Emergency access that is logged and reviewed | _(planned for M52)_ |
| See an application as another user sees it | _(planned for M52)_ |

### Audit and compliance

| Capability | Where |
| --- | --- |
| Audit of sign-in, sign-out and refused sign-in | [Sign-in audit](reference/security.md#sign-in-audit) |
| Audit of user and role changes | [Audit](reference/user-administration.md#audit) |
| Audit of publishing and activation | [Audit](reference/authoring.md#audit) |
| Audit of the operator's commands on a tenant | [Platform audit records](reference/tenant-setting-commands.md#platform-audit-records) |
| Audit records that nobody can change or delete | [Business](reference/keys-and-data-protection.md#business) |
| Security events in the logs | [Security events](reference/log-inventory.md#security-events) |
| Send the tenant's audit to its own security monitoring | _(planned for M57)_ |
| Alerts on bursts of refused sign-ins and other security events | _(planned for M32)_ |
| Security requirements tracked against a published standard | [Requirements](reference/asvs.md#requirements) |
| A final security sign-off and a yearly maturity rating | _(planned for M32)_ |
| A threat model kept up to date | [Threat model](reference/threat-model.md) |
| An independent penetration test | [Other evidence](reference/asvs.md#other-evidence) |
| A formal security certification for the service | _(planned for M32)_ |
| How each kind of data is classified and handled | [Data classification](reference/keys-and-data-protection.md#data-classification) |
| Data kept in its region | [Regions and cells](reference/cells.md#regions-and-cells) |
| Terms that each person accepts | [Accepting the terms](reference/legal.md#accepting-the-terms) |
| Export one person's data | _(planned for M47)_ |

### Keys and secrets

| Capability | Where |
| --- | --- |
| Sensitive fields encrypted under a key of the tenant | [Sensitive field values](reference/data-encryption.md#sensitive-field-values) |
| Rotate a tenant's keys | [Rotating the keys](reference/data-encryption.md#rotating-the-keys) |
| Encrypted backups | [Encrypted backups](reference/data-encryption.md#encrypted-backups) |
| The rest of the data encrypted at rest by the database or disk | [Business](reference/keys-and-data-protection.md#business) |
| Connection secrets stored encrypted | [Deployment bindings](reference/tenant-setting-commands.md#deployment-bindings) |
| Rotate the installation key | [Installation key](reference/tenant-setting-commands.md#installation-key) |
| Every secret rests on a cloud or customer key service | _(planned for M35)_ |
| A key that the customer holds for its own tenant | _(planned for M57)_ |
| Encrypted connections everywhere | [TLS](reference/installation.md#tls) |

### Change and release

| Capability | Where |
| --- | --- |
| Every save kept as a revision | [Revisions](reference/revisions-and-drafts.md#revisions) |
| Undo a revision | [Undo](reference/revisions-and-drafts.md#undo) |
| See what a revision changed | [Diff](reference/authoring-revisions.md#diff) |
| Two authors' edits to one resource never overwrite each other | [Versions](reference/authoring.md#versions) |
| An agent's change made on a draft and accepted all at once | [Drafts](reference/revisions-and-drafts.md#drafts) |
| Drafts for every author, merged on accept | _(planned for M51)_ |
| Compare two releases | _(planned for M51)_ |
| Try a change on a preview tenant before publishing | [Preview](reference/authoring.md#preview) |
| Development, test and production environments | _(planned for M51)_ |
| Move an application between tenants or installations | _(planned for M51)_ |
| A separate role to publish | [Settings](reference/authoring.md#settings) |
| A second person approves a publish | _(planned for M51)_ |
| Run an application's test scenarios | [Running the scenarios](reference/test-scenarios.md#running-the-scenarios) |
| A module's rules checked by its scenarios on every publish | [Contract scenarios](reference/module-contracts.md#contract-scenarios) |
| Run an application's tests in the organization's own build pipeline | _(planned for M51)_ |
| Release history | [Release history](reference/authoring.md#release-history) |
| Go back to an earlier release | [Activating a release](reference/authoring.md#activating-a-release) |
| Activate a release at a set time | _(planned for M51)_ |
| Turn a feature on for some people first | _(planned for M51)_ |
| A test copy of production data with personal data masked | _(planned for M51)_ |
| Start from a built-in application or template | [Application templates](reference/modules-and-templates.md#application-templates) |
| Built-in modules for standard processes | [Built-in modules](reference/built-in-modules.md) |
| Upgrade a module and keep the application's changes | _(planned for M29)_ |

### Operations

| Capability | Where |
| --- | --- |
| Logs, metrics and traces linked to each other | [Correlation](reference/telemetry.md#correlation) |
| Health checks of every host | [The answer](reference/health-checks.md#the-answer) |
| See a tenant's failed work, dead letters and schedules | [Views](reference/operator-console.md#views) |
| Retry, cancel or resolve failed work with a reason | [Commands](reference/operator-console.md#commands) |
| See every tenant of an installation with its health and usage | [Views](reference/cell-console.md#views) |
| A tenant's administrator is told about failed work | _(planned for M44)_ |
| An alert when a backup is late | [Daily backup](reference/tenant-backup-and-restore.md#daily-backup) |
| Back up one tenant | [Tenant backup](reference/tenant-backup-and-restore.md#tenant-backup) |
| Restore one tenant | [Tenant restore](reference/tenant-backup-and-restore.md#tenant-restore) |
| Every tenant backed up each day and kept 30 days | [Backups in a cell](reference/tenant-backup-and-restore.md#backups-in-a-cell) |
| A restore tried every week | [Weekly restore check](reference/tenant-backup-and-restore.md#weekly-restore-check) |
| Restore one tenant to a point in time | _(planned for M57)_ |
| Stated recovery point and recovery time | _(planned for M57)_ |
| Fail over to another region | _(planned for M57)_ |
| Several server replicas | [Shared state](reference/several-server-replicas.md#shared-state) |
| Several worker replicas | [Values](reference/installation.md#values) |
| Performance objectives and load tests | [Objectives](reference/performance.md#objectives) |
| Install on premises | [On premises](reference/installation.md#on-premises) |
| Install in the cloud | [In the cloud](reference/installation.md#in-the-cloud) |
| Upgrade the platform | [Upgrade](reference/upgrade.md#upgrade) |
| Upgrade the platform without downtime | _(planned for M57)_ |
| Tell tenants about maintenance ahead of time | _(planned for M57)_ |
| Quotas for each tenant | [Quotas](reference/tenant-quotas.md#quotas) |
| Usage measured for each tenant | [Usage](reference/tenant-quotas.md#usage) |
| Plans, payment and invoices | _(planned for M34)_ |
| Images and dependencies scanned for vulnerabilities | [Scans](development.md#scans) |
| Signed images, each with a bill of materials | _(planned for M32)_ |
| A hardening checklist for the operator | _(planned for M32)_ |

### Tenant administration

| Capability | Where |
| --- | --- |
| Invite people, disable them and give roles | [The users page](reference/user-administration.md#the-users-page) |
| Export all of the tenant's data | [Exports by a tenant administrator](reference/tenant-lifecycle.md#exports-by-a-tenant-administrator) |
| See the tenant's quotas and usage | [Export data and usage](reference/platform-pages.md#export-data-and-usage) |
| Add a host of the tenant's own | [Hosts by a tenant administrator](reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator) |
| Set the organization's sign-in | _(planned for M44)_ |
| Set connections and their secrets | _(planned for M44)_ |
| Set the tenant's mail | _(planned for M44)_ |
| Set the tenant's legal links | _(planned for M44)_ |
| See how much each application is used | _(planned for M54)_ |
| Each person chooses which notifications they get | _(planned for M43)_ |
