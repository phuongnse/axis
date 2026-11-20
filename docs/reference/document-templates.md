# Document templates

A purchase order, a contract or a certificate is a document that the business already writes in Word. This
page covers the `documentTemplate` resource that turns such a Word file into a PDF or a Word file for a
record, the process step and the page download that make it, a report as a PDF, the runner that makes
documents, and how an e-signature goes through a connector, as
[D157](../decisions/d157-files-reach-calls-and-emails-show-in-the-page-keep-their-versions-and-documents-come-from-word-templates.md)
agrees. The files that a document is written to are in [file fields](file-fields.md), and how a call or an
email carries one is in [files in calls](connector-and-package-calls.md#files-in-calls) and
[notifications](notifications.md#email).

## Document template resource

_(planned for M40)_

A `documentTemplate` resource holds a Word file for each language of the application, and the places in it
where values of a record go. The application keeps the files beside the resource, so a business person edits
them in Word and a release holds them with the rest of the application.

```json
{
  "id": "7d2e9c41-5a6b-4f08-b3c2-1e9a40d85f63",
  "kind": "documentTemplate",
  "name": "PurchaseOrderDocument",
  "formatVersion": 1,
  "label": { "textKey": "documents.purchaseOrder" },
  "entity": "PurchaseOrder",
  "files": {
    "en": "purchase-order.en.docx",
    "vi": "purchase-order.vi.docx"
  }
}
```

- **Entity or data source.** A template has exactly one of `entity` and `dataSource`. A template of an
  `entity` makes the document of one record. One of a `dataSource` makes a report, see
  [reports as PDF](#reports-as-pdf).
- **Files.** `files` maps each locale to a `.docx` file in the folder of the resource. The locales are
  those of the application, and each of them needs a file. A file that is not a Word document, or that holds
  macros or content linked from outside, is refused when the release is published.
- **Places.** A place is written `{{supplier.name}}` in the text of the Word file, in the body, a header or a
  footer. It names a field of the record, or a field reached through a `reference`, and Axis formats the
  value by its type and the language of the document, as a screen does. A `money` value shows its currency.
- **Repeated tables.** A table whose first row starts with `{{#lines}}` repeats that row for each row of the
  child collection `lines`, in the collection's order. The places in the row name fields of the child
  entity, and a `{{#lines}}` over a collection with no rows leaves the table with its heading only.
- **Conditions.** A paragraph or a table between `{{#if total > 1000000}}` and `{{/if}}` shows only when the
  [expression](expressions.md) is true for the record. Its scope is the record's fields.
- **Images.** `{{image signature}}` shows the image that a `file` field of the record holds, when it is a PNG
  or a JPEG, and nothing when the field is `null`.
- **Languages.** A document is made in one language, which a step or a download names. It is the application's
  default locale when none is named, and a locale without a file is refused.
- **Checks.** Publishing refuses a template when its entity or data source is unknown, when a locale has no
  file or a file that is not in `files`, when a place names a field that the entity does not have, when a
  repeat names something that is not a child collection, when an image place names a field that is not a
  `file` field, and when a repeat or a condition is not closed. Each is reported at the template, as the
  [resource diagnostic codes](diagnostic-codes-resources.md) page lists once the checks exist.
- **Who may edit.** Whoever may publish the application. The file is part of the release, so a document made
  from a release is always the one that release holds.

## Document step

_(planned for M40)_

A `makeDocument` operation step makes the document of the process's subject record and writes the file to a
field of that record. It looks like this:

```json
{
  "name": "makeOrder",
  "type": "operation",
  "operation": "makeDocument",
  "template": "PurchaseOrderDocument",
  "format": "pdf",
  "locale": "language",
  "output": { "file": "orderDocument" },
  "retry": { "attempts": 3, "backoff": "PT30S" },
  "next": "sendOrder"
}
```

- **`template`** names a `documentTemplate` whose entity is the subject entity.
- **`format`** is `pdf`, the default, or `docx`.
- **`locale`** is optional. It is a `text` expression over the subject record that gives a locale of the
  application, and the application's default locale when it is left out.
- **`output`** is optional. `file` names a `file` field of the subject entity that the step writes, and that
  an `updateRecord` `set` may name. Without it, the document is made for the `attachments` of a later
  [`notify`](notifications.md#notify-operation) step, which can make it again for one email.
- **How it runs.** The step runs like a [package operation](connector-and-package-calls.md#package-operations): it stages
  the call, the worker has [the document runner](#the-document-runner) make the file outside any transaction,
  and a separate transaction attaches the file to the subject's field and takes `next`. A step that is run
  again makes the document again, and the file it replaced is cleaned up as
  [storage and clean-up](file-fields.md#storage-and-clean-up) says.
- **Failure.** A runner that does not answer, a document over `Files:MaxSize` and a runner that refuses the
  template are failures of the call, retried by `retry` as a package call is. After the last attempt the
  step fails and the instance needs an operator, as for any operation.
- **History.** Step history holds the file id and the format, never the name, the values or the bytes.
- **Compile checks.** The template exists and has the subject's entity, `format` and `locale` have the shapes
  above, `output.file` is a `file` field that no other output of the step writes, and the process has a
  subject. A step has `template` and `next`, and no `set`, `connector` or `input`.

## Page download

_(planned for M40)_

A `detail` or `form` widget may carry a `downloadDocument` action that gives the person the document of the
record on the page. It is a Secondary button, see [actions and controls](actions-and-controls.md#buttons), and
it takes a label text key, a `template` and a `format`.

| Route | What it does |
| --- | --- |
| `GET /api/apps/{app}/entities/{entity}/records/{id}/documents/{template}?format=&locale=` _(planned for M40)_ | Makes the document of a record and answers it as an attachment |

- **Checks.** The route checks in the order of the record routes: sign-in (`401`), the path (`404`), the grant
  (`403`), then the query (`400`). The user needs the `read` grant on the entity, and the record filters
  apply, so a record the user cannot read is a `404`. A template that the active release does not have, or
  whose entity is another, is a `404` too.
- **What the user sees.** The document holds only the fields that the user may read. A place for a field that
  the user may not read stays empty, as a record read leaves that field out.
- **Answer.** `200` with the file, `Content-Disposition: attachment` with a name made from the template's
  `name`, the media type of the format and `X-Content-Type-Options: nosniff`, as
  [headers](headers-and-rate-limits.md#security-headers) say. The route counts in the session partition of the
  [rate limits](headers-and-rate-limits.md#rate-limits).
- **Runner.** The server asks the runner for the file and holds no copy of it. A runner that does not answer in
  time is a `503`.
- **Audit.** `document.made`, with the actor and the details `{ "template", "format", "record" }`, never a
  value or the bytes.

## Reports as PDF

_(planned for M40)_

A report is a template of a `dataSource` whose repeated table fills from the data source's rows, so a list
of purchase orders, or the lines of a quarter, becomes a PDF in the layout that the business already uses.

| Route | What it does |
| --- | --- |
| `GET /api/apps/{app}/data-sources/{dataSource}/documents/{template}?format=&sort=&search=&filter=` _(planned for M40)_ | Makes the report of a data source's rows and answers it as an attachment |

- **Places.** `{{#rows}}` opens the repeated table, and the places in its row name the data source's columns.
  A place outside the table names a parameter of the data source or a measure of its aggregates.
- **Rows.** The rows are the ones that the data source gives the user, under the same grant, the same record
  filters and the same `sort`, `search` and `filter` as [its rows route](data-sources.md#endpoint), and as an
  [export](import-export.md#export) reads them. The report stops at the most rows of an export, and says so
  in its last line.
- **Checks.** They are those of the [document route](#page-download), with the grant of the data source.
- **Where it is offered.** A `table` widget may carry a `downloadDocument` action with a `template` of its
  data source, and the page's [print layout](frontend.md#printing) is the other way to put a list on paper.

## The document runner

_(planned for M40)_

Word files come from outside Axis and carry structure that a parser can be made to choke on. So Axis makes
documents in a runner that has a sandbox, never in the server or the worker.

- **One mechanism.** The runner runs under the sandbox of the runners of an application's packages, gVisor,
  with an empty environment, a read-only file system, the resource limits of
  [package runners](extension-packages.md#package-runners), and no network route at all, because a document
  needs none. See [in package runners](extension-package-limits.md#in-package-runners).
- **What it gets.** One call carries the template's file, the values of the places as JSON and the images
  that the document shows, and the runner answers one file. It keeps nothing after the call and serves one
  tenant's call at a time, so a document never holds another tenant's data.
- **Who asks.** The worker for a step, and the server for a download or a report. The host setting
  `Documents:Runner` names the runner's address, and `Documents:Timeout` how long a call may take, see
  [data settings](host-settings.md#data-settings). A call with no runner named fails with a message that names
  the setting.
- **Limits.** The output is at most `Files:MaxSize`, and a template that loops, nests or grows without end
  stops at the timeout and the runner's memory limit. The call then fails, and the runner process is dropped
  and started again.
- **Fonts.** The runner holds the fonts of the languages of the application, so a Vietnamese document shows
  its marks the same on every installation.

## E-signature through a connector

_(planned for M40)_

A contract is signed by a person outside the application, through a signing service such as a Vietnamese
certificate authority or DocuSign. Axis holds no signing service of its own. A process puts the pieces that
exist today together, and the signed file ends in a `file` field of the record. The process learns that the
contract is signed by asking the service, not by waiting for its call.

1. A [`makeDocument`](#document-step) step makes the contract and writes it to a `file` field.
2. A `callConnector` step sends that file to the signing service. The connector's operation has an input of the
   type `file`, see [files in calls](connector-and-package-calls.md#files-in-calls), and the service answers with the id
   of its signing request, which the step writes to a field.
3. A `timer` step waits, then a second `callConnector` step asks the service for the status of the request and
   writes it to a field.
4. A `decision` step reads that status. While it is still pending, the step goes back to the timer. When it is
   signed, the process goes on. When it is declined or expired, the process takes its own path, such as a task
   for a person.
5. A third `callConnector` step fetches the signed file. The operation's `file` output writes it to another
   `file` field, and the outcome's transaction attaches it to the record.

```json
[
  {
    "name": "makeContract",
    "type": "operation",
    "operation": "makeDocument",
    "template": "ContractDocument",
    "output": { "file": "contractDocument" },
    "next": "sendForSignature"
  },
  {
    "name": "sendForSignature",
    "type": "operation",
    "operation": "callConnector",
    "connector": { "name": "SigningService", "operation": "sendForSignature" },
    "input": { "document": "contractDocument", "signerEmail": "customer.email" },
    "output": { "requestId": "signingRequestId" },
    "next": "pauseForSigner"
  },
  { "name": "pauseForSigner", "type": "timer", "wait": "PT4H", "next": "askStatus" },
  {
    "name": "askStatus",
    "type": "operation",
    "operation": "callConnector",
    "connector": { "name": "SigningService", "operation": "getStatus" },
    "input": { "requestId": "signingRequestId" },
    "output": { "status": "signingStatus" },
    "next": "checkStatus"
  },
  {
    "name": "checkStatus",
    "type": "decision",
    "branches": [
      { "name": "signed", "when": "signingStatus == 'signed'", "next": "fetchSigned" },
      { "name": "refused", "when": "signingStatus == 'declined' or signingStatus == 'expired'", "next": "chaseSigner" }
    ],
    "otherwise": "pauseForSigner"
  },
  {
    "name": "fetchSigned",
    "type": "operation",
    "operation": "callConnector",
    "connector": { "name": "SigningService", "operation": "fetchSigned" },
    "input": { "requestId": "signingRequestId" },
    "output": { "signedDocument": "signedContract" },
    "next": "done"
  }
]
```

- **Why not wait for the call.** A call to an [inbound endpoint](inbound-endpoints.md) always creates a new
  record and raises its event about that record, and an event wakes only the instances that wait on its own
  record, see [wait for event](process-events.md#waking-waiting-instances). So the signing service's call cannot wake
  the process that waits on the contract.
- **A wake from the callback.** _(planned for M45)_ Waking a waiting process from another system's callback,
  and matching an event by another key, replace the timer loop with a `waitEvent` step that the service's call
  wakes. They are in the [capability map](../capabilities.md#other-systems-calling-in), and M40 does not
  depend on them.
- **No signing in Axis.** The service checks who signs and holds the certificate. Axis sends a file and keeps
  the file that comes back, so a signature's legal weight is the service's.
- **The signed file.** It goes to a `file` field of its own, so the unsigned contract stays as it was.
