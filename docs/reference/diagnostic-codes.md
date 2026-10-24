# Diagnostic codes

Detailed reference for the diagnostics that the [configuration pipeline](configuration.md)
reports, and the table of every code. A new code gets the next free number and a new row at the
end of the table. A unit test compares the table with `DiagnosticCodes` and
`ExpressionDiagnosticCodes`.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

Diagnostics always carry `file`, `resourceId` (when the file has a readable
ID), `path`, `code` and a message. `file` is relative to the application
folder and uses `/` separators, or is empty when the diagnostic concerns the
whole application folder. `path` is a JSON Pointer (RFC 6901) into that file,
such as `/fields/0/type`, or empty when the problem concerns the whole file or
the whole folder. Messages never contain absolute paths or exception text,
because they are shown to application authors. Codes have the form `AXCnnnn`
and never change meaning. A retired code is never reported again and keeps its row. Compile reports all diagnostics, not just the first,
sorted by file and then path.

| Code | Meaning |
| --- | --- |
| `AXC0001` | The file is not valid JSON. |
| `AXC0002` | The resource has no `kind`, or `kind` is not a string. |
| `AXC0003` | The `kind` is not a known resource kind. |
| `AXC0004` | The resource does not match the JSON Schema for its kind. |
| `AXC0005` | Another resource already uses this `id`. |
| `AXC0006` | Another resource of the same kind already uses this `name`. |
| `AXC0007` | The folder has no `application` manifest. |
| `AXC0008` | The folder has more than one `application` manifest. |
| `AXC0009` | An `application` resource is not `application.json` at the folder root, or the root `application.json` has another kind. |
| `AXC0010` | The file could not be read, for example because access is denied. |
| `AXC0011` | Another field of the same entity already uses this `name`, ignoring letter case. |
| `AXC0012` | A reference or child collection field's `target` names no loaded entity. Not reported when the target names an entity file in the folder that was not loaded because of its own errors. |
| `AXC0013` | A field property does not fit the field's type, or its value is outside what storage accepts. This includes `required` on a computed field, and an `expression` on a `reference` or `child-collection` field. A `sequence` on a field that is not `text`, is `required`, has an `expression` or belongs to a child entity is reported once, at `/fields/{i}/sequence`. |
| `AXC0014` | A field lacks a property its type needs: `target` on a reference or child collection, `values` on an enum. |
| `AXC0015` | An entity table has a column whose field was removed and that no pending `renameField` or `dropField` migration covers. Reported at `/fields` of the entity file. |
| `AXC0016` | A field changed in a way its existing column cannot follow, such as a new type, a shorter `maxLength` or a removed enum value. |
| `AXC0017` | An entity provisioned for the application is missing from it and no pending `dropEntity` migration covers it. Reported at `application.json` with an empty path. |
| `AXC0018` | The entity's `id` is already provisioned for another application. Reported at `/id` of the entity file. |
| `AXC0019` | The application folder could not be listed: it does not exist, it cannot be opened, or one of its subfolders cannot be opened. Reported with an empty `file` and `path`, as the only diagnostic; nothing in the folder is loaded. |
| `AXC0020` | The application's `name` is active for another application `id`, ignoring letter case. Reported at `/name` of `application.json`, as the only diagnostic; nothing is provisioned or activated. |
| `AXC0021` | A widget's `entity` names no loaded entity. Reported at `/widgets/{i}/entity`. |
| `AXC0022` | Retired. It was the removed `formPage` property of a table widget. It is never reported and the number is never reused. |
| `AXC0023` | A navigation entry names no loaded page. Reported at `/navigation/{i}/page`. |
| `AXC0024` | A site path is reserved by the platform, or another site of the application already uses it. Reported at `/path` of the later file. |
| `AXC0025` | A site's default or fallback locale is not in `available`, or an available locale has no `text` resource. Reported at that locale. |
| `AXC0026` | Another `text` resource already holds this locale, ignoring letter case. Reported at `/locale` of the later file. |
| `AXC0027` | A text key that another locale has is missing from this locale. Reported at `/texts`, naming the key and a locale that has it. |
| `AXC0028` | No locale has the text key of this label. Reported at the label's `/textKey`. |
| `AXC0029` | The entity's `displayField` names no required `text` field of the entity. Reported at `/displayField`. |
| `AXC0030` | A reference field's target entity has no `displayField`. Reported at `/fields/{i}/target`. |
| `AXC0031` | A site path is active for another application. Reported at `/path` of the site file; nothing is provisioned or activated. |
| `AXC0032` | A seed's `entity` names no loaded entity. Reported at `/entity`. |
| `AXC0033` | A seed value is one the record API would reject, or the seed record could not be inserted or updated, for example because a reference names no record. Reported by the startup step at `/records/{i}/values/<field>` of the seed file, or at `/records/{i}` when the stored schema does not match the active model. |
| `AXC0034` | An earlier seed record of the application already uses this record id. Reported at `/records/{i}/id` of the later record, naming the file of the first one. |
| `AXC0035` | An expression has a syntax error: an unknown character, a bad token or literal, or text the grammar does not allow. Reported at the JSON Pointer of the expression string, with the character position in the message. See [expression diagnostics](expressions.md#diagnostics). |
| `AXC0036` | An expression is longer than 2,000 characters. Reported at the JSON Pointer of the expression string. |
| `AXC0037` | An expression nests deeper than 32 levels. Reported at the JSON Pointer of the expression string, with the character position in the message. |
| `AXC0038` | An expression has more than 500 syntax nodes. Reported at the JSON Pointer of the expression string, with the character position in the message. |
| `AXC0039` | The child entity is already owned by another `child-collection` field. Reported at `/fields/{i}/target` of the later field, naming the owner. |
| `AXC0040` | A reference field's target is a child entity. Reported at `/fields/{i}/target`, naming the owner. |
| `AXC0041` | A child entity has a `reference` or `child-collection` field. Reported at `/fields/{i}/type` of that field, naming the owner. |
| `AXC0042` | A data source's `entity` names no loaded entity. Reported at `/entity`. Not reported when the name is an entity file that was not loaded because of its own errors. |
| `AXC0043` | A data source field's `path` names no field of the data source's entity or of a reference's target, goes through a field that is not a `reference`, takes more than 3 hops, or ends at a `child-collection` field, which has no column. The message says which. Reported at `/fields/{i}/path`. |
| `AXC0044` | An earlier field of the same data source already uses this `name`, compared exactly. Reported at `/fields/{i}/name` of the later field. |
| `AXC0045` | A data source's `sort` names no projected field, or names a projected field whose path ends at a `reference`. For a grouped data source, it names no group field or measure, or names a group field whose path ends at a `reference`. Reported at `/sort`. |
| `AXC0046` | An expression names a field that is not in its scope, including an unknown field after a `.`. Outside a data source filter and a process condition, every `.` path is reported this way. Reported at the JSON Pointer of the expression string, with the character position in the message. See [expression diagnostics](expressions.md#diagnostics). |
| `AXC0047` | An expression gives an operator, function or rule operands of types it does not accept, such as `'a' < 'b'`, `quantity and true`, `length(1)` or `IsPositive('a')`. This includes a `date('…')` or `dateTime('…')` text that is not a valid date or date-time, such as `date('2026-13-45')`, in every use. It also includes a child collection used anywhere but as the first argument of an aggregate, such as `lineItems == null`, and an aggregate over a field that is not a child collection, such as `sum(title, amount)`. In a data source filter or a process condition it also includes a `.` after a field that is not a `reference`, such as `name.x`. In a data source filter it also includes a `.` after a parameter. Reported at the JSON Pointer of the expression string, with the character position of the operator or the call in the message. |
| `AXC0048` | An expression's type does not fit the type its use needs, such as an integer where a validation needs a boolean. Reported at the JSON Pointer of the expression string. The message names both types. |
| `AXC0049` | A text literal compared with an enum is not one of the field's `values`. Reported at the JSON Pointer of the expression string, with the character position of the literal in the message. |
| `AXC0050` | An expression calls a function or rule that does not exist, such as `foo(1)`. Reported at the JSON Pointer of the expression string, with the character position of the call in the message. |
| `AXC0051` | An expression calls a function or rule with the wrong number of arguments, such as `round(1.5)`, `sum(lineItems)` or `IsPositive()`. Reported at the JSON Pointer of the expression string, with the character position of the call in the message. The message names the expected count. The SQL check of a migration value that is not type-checked reports it too, such as `abs()`. |
| `AXC0052` | A validation's `field` names no field of the entity. Reported at `/validations/{i}/field`. |
| `AXC0053` | A data source filter uses something outside the SQL subset, such as `/`, `lower(name)`, an aggregate such as `count(lines)`, or a call to a rule whose expression uses `lower`. So does a filter whose rule calls inline to more than 2,000 syntax nodes. Reported at `/filter`, with the character position of the operator or the call in the message. A message about a rule call names the rule the filter calls. A migration's `fill` or `convert` value uses something outside the SQL subset in the same way, such as `upper(title)`, and is reported at `/changes/{i}/value`. See [SQL subset](expressions.md#sql-subset). |
| `AXC0054` | A data source parameter's `name` is also a field of the data source's entity, is `page`, `pageSize` or `sort`, or is already used by an earlier parameter of the same data source, all ignoring letter case. Reported at `/parameters/{i}/name`. |
| `AXC0055` | Rules call each other in a cycle, such as A → B → A. Reported once per cycle, at `/expression` of the rule where the cycle starts in path order. The message names every rule in the cycle. |
| `AXC0056` | An earlier parameter of the same rule already uses this `name`, ignoring letter case. Reported at `/parameters/{i}/name` of the later parameter. |
| `AXC0057` | A rule's `name` is the name of a built-in function, ignoring letter case, such as `round`. Reported at `/name`. |
| `AXC0058` | A path in a data source filter or a process condition takes more than 3 hops, such as `a.b.c.d.name`. Reported at the JSON Pointer of the expression string, such as `/filter` or `/steps/0/branches/0/when`, with the character position of the `.` that goes past the limit in the message. See [expression diagnostics](expressions.md#diagnostics). |
| `AXC0059` | A widget's `dataSource` names no loaded data source. Reported at `/widgets/{i}/dataSource`. Not reported when the name is a data source file that was not loaded because of its own errors. |
| `AXC0060` | A `table` widget names both `entity` and `dataSource`, or neither, or a `form` widget names both `form` and `entity`, or neither. Reported at `/widgets/{i}`. Or a `form` widget names a `dataSource`, or a table, a chart or a `kpi` leaves a required parameter of its data source unbound, because it has no input for it. Those are reported at `/widgets/{i}/dataSource`, and the message names the parameter. Or an entry of a table's, a chart's or a `kpi`'s `parameters` names no data source parameter or page parameter, repeats a key ignoring letter case, binds parameters of different types, or binds a required data source parameter to an optional page parameter, reported at `/widgets/{i}/parameters/{key}`. Or `parameters` is on a widget that is not a table, a chart or a `kpi` over a data source, reported at `/widgets/{i}/parameters`. Or a `table` widget names a `form`, reported at `/widgets/{i}/form`. Or a `taskInbox` or `processInspector` widget names an `entity`, `dataSource` or `form`, reported at that property. Or a `table`, `taskInbox`, `processInspector` or `kpi` widget has `actions`, reported at `/widgets/{i}/actions`. Or a `kpi` widget names an `entity` or a `form`, reported at that property. Or a `form`, `taskInbox`, `processInspector` or `kpi` widget has `rowActions` or `toolbarActions`, reported at that property. Or a table over a grouped data source has `rowActions`, because a group row has no actions, reported at `/widgets/{i}/rowActions`. |
| `AXC0061` | A data source's `aggregate.groupBy` entry names no projected field, compared exactly. Reported at `/aggregate/groupBy/{i}`. |
| `AXC0062` | A data source measure's `name` is also a group field, or is already used by an earlier measure, compared exactly. Reported at `/aggregate/measures/{i}/name` of the later measure. |
| `AXC0063` | A data source measure is invalid. A `count` with a `field`, or a `sum`, `min` or `max` without one, is reported at `/aggregate/measures/{i}`. A `field` that names no projected field, or whose type the function does not take, is reported at `/aggregate/measures/{i}/field`. `sum` takes an integer or a decimal. `min` and `max` take an integer, a decimal, a date or a date-time. |
| `AXC0064` | A data source's `entity` is a child entity. Reported at `/entity`, naming the owner. |
| `AXC0065` | Rule calls nest more than 8 deep, such as R1 → R2 → … → R9. Reported once, at `/expression` of the lowest rule past the limit, not at the rules or validations that call it. The message names the rules in the chain in call order. A rule in a cycle, or that calls into one, gets only `AXC0055`. See [cost bounds](expressions.md#cost-bounds). |
| `AXC0066` | A field's `sequence` names no loaded sequence, ignoring letter case. Reported at `/fields/{i}/sequence`. Not reported when the name is a sequence file that was not loaded because of its own errors, or when the field is not `text`. |
| `AXC0067` | A process's `entity` names no loaded entity. Reported at `/entity`. Not reported when the name is an entity file that was not loaded because of its own errors. |
| `AXC0068` | A process's `entity` is a child entity. Reported at `/entity`, naming the owner. |
| `AXC0069` | An earlier step of the same process already uses this `name`, ignoring letter case. Reported at `/steps/{i}/name` of the later step, naming the first one. The later step is left out of `AXC0071`, `AXC0072` and `AXC0073`. |
| `AXC0070` | A process's `start`, a branch `next`, an `otherwise`, an operation's `next` or a task outcome's `next` names no step of the process, ignoring letter case. Reported at that property, such as `/start`, `/steps/{i}/branches/{j}/next`, `/steps/{i}/next` or `/steps/{i}/outcomes/{j}/next`. |
| `AXC0071` | A process step cannot be reached from `start`. Reported at `/steps/{i}`. Not checked when `start` names no step. |
| `AXC0072` | A process step has no path to an `end` step. Reported at `/steps/{i}`. Not reported for a step with a transition to an unknown step, which is already `AXC0070`. |
| `AXC0073` | Process steps form a cycle, such as a → b → a. Reported at the transition that closes the cycle, walking the steps in file order and each step's transitions in order: the branches, then `otherwise`, then `next`, then the outcomes. The message names the steps in the cycle. See [Limits in M3](processes.md#limits-in-m3). |
| `AXC0074` | A form's `entity` names no loaded entity. Reported at `/entity`. Not reported when the name is an entity file that was not loaded because of its own errors. |
| `AXC0075` | A form section's `field` names no field of the form's entity, ignoring letter case. Reported at `/sections/{i}/fields/{j}/field`. |
| `AXC0076` | An earlier entry of the same form already lists this field, in any section and ignoring letter case. Reported at `/sections/{i}/fields/{j}/field` of the later entry, naming the first one. |
| `AXC0077` | A `form` widget's `form` names no loaded form. Reported at `/widgets/{i}/form`. Not reported when the name is a form file that was not loaded because of its own errors. |
| `AXC0078` | A process operation step's `operation` is not `updateRecord`, `callConnector`, `raiseEvent` or a package operation written `<package>/<operation>`, matched exactly. Reported at `/steps/{i}/operation`. Its other properties are then not checked. |
| `AXC0079` | A key of an `updateRecord` `set` names no field of the subject entity, or a field an earlier key of the step already sets, ignoring letter case. Reported at `/steps/{i}/set/{field}`. |
| `AXC0080` | A key of an `updateRecord` `set` names a computed field, a sequence field or a child collection, which a process cannot set. Reported at `/steps/{i}/set/{field}`. |
| `AXC0081` | A process task step's `assignee` has both or neither of `user` and `role`. Reported at `/steps/{i}/assignee`. |
| `AXC0082` | A process task step's `form` names no loaded form, ignoring letter case. Reported at `/steps/{i}/form`. Not reported when the name is a form file that was not loaded because of its own errors. |
| `AXC0083` | A process task step's `form` is over another entity than the process's subject entity. Reported at `/steps/{i}/form`, naming both entities. Not checked when either entity is unknown. |
| `AXC0084` | A process task step's `dueIn` is not a positive ISO 8601 duration in the accepted form: whole-number weeks (`W`), or days (`D`) with an optional `T` part of hours (`H`), minutes (`M`) and seconds (`S`), such as `P2W`, `P3D`, `PT4H` or `P1DT12H`. Years and months are `AXC0084` until calendar durations come with the [time zone rule](../decisions/d22-current-time-and-time-zones.md), because they have no fixed length. Reported at `/steps/{i}/dueIn`. |
| `AXC0085` | An earlier outcome of the same task step already uses this `name`, ignoring letter case. Reported at `/steps/{i}/outcomes/{j}/name` of the later outcome, naming the first one. |
| `AXC0086` | A computed field calls `now()`, also inside an aggregate's item expression, such as `count(lines, dueAt < now())`. A stored value cannot depend on the current time. Reported at `/fields/{i}/expression`, with the character position of the call in the message. See [expression diagnostics](expressions.md#diagnostics). |
| `AXC0087` | A `form` widget action's, or a `table` row action's, `process` names no loaded process. Reported at `/widgets/{i}/actions/{j}/process` or `/widgets/{i}/rowActions/{j}/process`. Not reported when the name is a process file that was not loaded because of its own errors. |
| `AXC0088` | A `form` widget action's, or a `table` row action's, `process` is over another entity than the widget's records. Reported at `/widgets/{i}/actions/{j}/process` or `/widgets/{i}/rowActions/{j}/process`, naming both entities. Not checked when the widget's entity is unknown. |
| `AXC0089` | A process task step's `assignee.role` names no declared role, compared exactly. Reported at `/steps/{i}/assignee/role`. Not reported when the name is a role file that was not loaded because of its own errors. |
| `AXC0090` | A policy's `roles` entry names no declared role, compared exactly. Reported at `/roles/{i}`. Not reported when the name is a role file that was not loaded because of its own errors. |
| `AXC0091` | A policy grant is badly shaped. It has no target or more than one of `entity`, `dataSource`, `process` and `endpoint`, reported at `/grants/{i}`. It has an action its target's kind does not take, reported at `/grants/{i}/actions/{j}`: an entity takes `read`, `create`, `update` and `delete`, a data source `read`, a process `start`, `inspect`, `cancel`, `retry` and `resolve`, and an endpoint `invoke`. Or a `dataSource`, `process` or `endpoint` grant has a `filter` or `fields`, reported at `/grants/{i}/filter` or `/grants/{i}/fields`. The message names the target and the allowed actions. |
| `AXC0092` | A policy grant's `entity`, `dataSource`, `process` or `endpoint` names no loaded resource of that kind, ignoring letter case. Reported at `/grants/{i}/entity`, `/grants/{i}/dataSource`, `/grants/{i}/process` or `/grants/{i}/endpoint`. Not reported when the name is a file that was not loaded because of its own errors. |
| `AXC0093` | A policy grant's `entity` is a child entity. Reported at `/grants/{i}/entity`, naming the owner. Child rows are read and written through their owner's grants. |
| `AXC0094` | A policy grant's `fields` entry names no field of the grant's entity, ignoring letter case. Reported at `/grants/{i}/fields/{j}`. |
| `AXC0095` | An expression that is not a policy filter calls `currentUser()`, such as a validation, a computed field, a data source filter, a process condition or a rule body. A rule argument inside a policy filter may call it. Reported at that expression's path, with the character position of the call in the message. See [expression diagnostics](expressions.md#diagnostics). |
| `AXC0096` | An earlier operation of the same connector already uses this `name`, or an earlier field of the same operation's `input` or `output` already uses this field `name`, ignoring letter case. Reported at `/operations/{i}/name`, `/operations/{i}/input/{j}/name` or `/operations/{i}/output/{j}/name` of the later one. |
| `AXC0097` | A connector operation's `path` has an unbalanced `{` or `}`, or a placeholder other than `{idempotencyKey}`, such as `{orderId}`. Reported at `/operations/{i}/path`. |
| `AXC0098` | A connector operation's `lookup` names no operation of the connector, ignoring letter case, or names its own operation. Or the operation it names is not a `GET`, has no `{idempotencyKey}` in its `path`, or has other outputs than the operation it serves. The message says which. Reported at `/operations/{i}/lookup`. |
| `AXC0099` | A connector's `timeout` is not a positive ISO 8601 duration of at most `PT2M`, in the form a task's `dueIn` takes, such as `PT30S`. Reported at `/timeout`. |
| `AXC0100` | The tenant a release is activated for lacks a binding a connector names, or holds it with another kind. `baseUrl` needs a `url` binding, and `auth` needs a `secret` binding. Reported by activation at `/baseUrl/binding`, `/auth/token/binding` or `/auth/value/binding` of the connector file. The same applies to the `capabilities` of a package operation that a process step uses: `urls` need `url` bindings and `secrets` need `secret` bindings, reported at `/steps/{i}/operation` of the process file. The message names the tenant and the binding, never its value. Nothing is provisioned or activated. |
| `AXC0101` | Outside Development, a connector's `baseUrl` binding is not an `https` URL. Reported by activation at `/baseUrl/binding` of the connector file. The same applies to a `url` binding in the `capabilities` of a package operation that a process step uses, reported at `/steps/{i}/operation` of the process file. The message names the tenant and the binding, never its value. Nothing is provisioned or activated. |
| `AXC0102` | A process `callConnector` step's `connector` `name` names no loaded connector, ignoring letter case. Reported at `/steps/{i}/connector/name`. Not reported when the name is a connector file that was not loaded because of its own errors. Its `input` and `output` are then not checked. |
| `AXC0103` | A process `callConnector` step's `connector` `operation` names no operation of its connector, ignoring letter case. Reported at `/steps/{i}/connector/operation`. Its `input` and `output` are then not checked. |
| `AXC0104` | A key of a `callConnector` step's `input` names no input of the operation, or an input an earlier key of the step already names, ignoring letter case. Reported at `/steps/{i}/input/{key}`. |
| `AXC0105` | A required input of the operation a `callConnector` step calls has no key in the step's `input`. Reported at `/steps/{i}/input`, once for each missing input. |
| `AXC0106` | A key of a `callConnector` step's `output` names no output of the operation, or an output an earlier key of the step already names, ignoring letter case. Reported at `/steps/{i}/output/{key}`. |
| `AXC0107` | A value of a `callConnector` step's `output` names no field of the subject entity, or a field an earlier output of the step already writes, ignoring letter case. Reported at `/steps/{i}/output/{key}`. |
| `AXC0108` | A value of a `callConnector` step's `output` names a computed field, a sequence field or a child collection, or a field whose type does not fit the output's. An output fits a field of the same type, and an `integer` output also fits a `decimal` field. Reported at `/steps/{i}/output/{key}`. |
| `AXC0109` | A `callConnector` step's retry `backoff` is not a positive ISO 8601 duration in the form `dueIn` accepts, such as `PT30S` or `P1D`. Reported at `/steps/{i}/retry/backoff`. |
| `AXC0110` | An event's `entity` names no loaded entity. Reported at `/entity`. Not reported when the name is an entity file that was not loaded because of its own errors. |
| `AXC0111` | An event's `entity` is a child entity. Reported at `/entity`, naming the owner. |
| `AXC0112` | An earlier field of the same event already uses this field `name`, ignoring letter case. Reported at `/fields/{i}/name` of the later one, naming the first. |
| `AXC0113` | An event field's `type` is `enum`, `reference` or `child-collection`. An event field is `text`, `integer`, `decimal`, `boolean`, `date` or `date-time`. Reported at `/fields/{i}/type`. |
| `AXC0114` | An endpoint's `entity` names no loaded entity. Reported at `/entity`. Not reported when the name is an entity file that was not loaded because of its own errors. |
| `AXC0115` | An endpoint's `entity` is a child entity. Reported at `/entity`, naming the owner. |
| `AXC0116` | An endpoint's `process` names no loaded process. Reported at `/process`. Not reported when the name is a process file that was not loaded because of its own errors. |
| `AXC0117` | An endpoint's `process` is over another entity than the endpoint's `entity`. Reported at `/process`, naming both entities. |
| `AXC0118` | An earlier input field of the same endpoint already uses this `name`, ignoring letter case. Reported at `/input/{i}/name` of the later one, naming the first. |
| `AXC0119` | A `child-collection` input field's `target` is not a child entity. Reported at `/input/{i}/target`. |
| `AXC0120` | An endpoint's `values` key names no field of the entity, or a field that an earlier key already maps, ignoring letter case. Reported at `/values/{key}`. |
| `AXC0121` | An endpoint's `values` key names a computed field, or a field with a `sequence` or `creator`, which the server sets. Reported at `/values/{key}`. |
| `AXC0122` | An endpoint maps a `child-collection` field to something other than the name of a `child-collection` input with the same target. Reported at `/values/{key}`. |
| `AXC0123` | An endpoint's `output` entry names no field of the entity, ignoring letter case. Reported at `/output/{i}`. |
| `AXC0124` | An endpoint's `wait` is not a valid ISO 8601 duration, is zero, or is above `PT30S`. Reported at `/wait`. |
| `AXC0125` | A `raiseEvent` step's `event` names no loaded event, ignoring letter case. Reported at `/steps/{i}/event`. Not reported when the name is an event file that was not loaded because of its own errors. |
| `AXC0126` | A `raiseEvent` step's `event` is about another entity than the process's. Reported at `/steps/{i}/event`, naming both entities. Not checked when either entity is unknown. |
| `AXC0127` | A key of a `raiseEvent` step's `fields` names no payload field of the event, or a field an earlier key of the step already names, ignoring letter case. Reported at `/steps/{i}/fields/{key}`. |
| `AXC0128` | A payload field of the event that no key of a `raiseEvent` step's `fields` names. Reported at `/steps/{i}/fields`, once for each missing field. |
| `AXC0129` | A trigger's `process` names no loaded process, ignoring letter case. Reported at `/process`. Not reported when the name is a process file that was not loaded because of its own errors. |
| `AXC0130` | A trigger's `event` names no loaded event, ignoring letter case. Reported at `/event`. Not reported when the name is an event file that was not loaded because of its own errors. |
| `AXC0131` | A trigger's `event` is about another entity than the process's subject entity. Reported at `/event`, naming both entities. Not checked when either entity is unknown. |
| `AXC0132` | An extension package manifest, `axis-package.json`, is missing, cannot be read, is not valid JSON or does not match its schema. Reported at startup by the host, at the failing JSON Pointer. |
| `AXC0133` | An extension package's folder names differ from its manifest's `id` or `version`. Reported at startup by the host, at `/id` or `/version`. |
| `AXC0134` | An extension package's `assembly` file is missing or cannot be loaded. Reported at startup by the host, at `/assembly`. |
| `AXC0135` | An extension package assembly's informational version, without build metadata after a `+`, differs from the manifest's `version`. Reported at startup by the host, at `/version`. |
| `AXC0136` | An extension package operation's `type` is not a class in the package assembly that implements `IOperation`, is not abstract and has a public parameterless constructor. Reported at startup by the host, at `/operations/{i}/type`. |
| `AXC0137` | An extension package operation name, or a field name in one operation's `input` or `output`, is already used, ignoring letter case. Reported at startup by the host, at the `name` of the later one. |
| `AXC0138` | An extension package operation's `timeout` is not a positive ISO 8601 duration of at most `PT2M`. Reported at startup by the host, at `/operations/{i}/timeout`. |
| `AXC0139` | An application's `packages` entry names a package id with no installed version. Reported in `application.json` at `/packages/{i}/id`. |
| `AXC0140` | An application's `packages` entry has a `version` range that is not an exact version `X.Y.Z`, `^X.Y.Z` or `~X.Y.Z`, with no leading zeros. Reported in `application.json` at `/packages/{i}/version`. |
| `AXC0141` | No installed version of a package is in its `packages` entry's `version` range. Reported in `application.json` at `/packages/{i}/version`, listing the installed versions. Not checked when the package is unknown or the range is invalid. |
| `AXC0142` | A package id appears in more than one `packages` entry. Reported in `application.json` at `/packages/{i}/id` of each later entry, which is not checked further. |
| `AXC0143` | A data change trigger's `entity` names no loaded entity. Reported in the trigger at `/entity`. |
| `AXC0144` | A data change trigger's `entity` is not the process's subject entity. Reported in the trigger at `/entity`. |
| `AXC0145` | A name in a data change trigger's `fields` names no field of the entity, or names a computed field. Reported in the trigger at `/fields/{i}`. |
| `AXC0146` | A release that is active, or has a running or waiting process instance, pins an extension package version that is not installed. Reported at the start of the server and the worker, in that release's stored `application.json` at `/packages/{i}/version`. It names the tenant, application, release, package id and version, and stops the start. A [rollback](authoring.md#rollback) of a release that pins a version that is not installed gets the same code and is refused. |
| `AXC0150` | A process package operation step's `operation` names a package that the application does not list in `packages`, matched exactly. Not reported for a listed package that failed pinning (`AXC0139` to `AXC0142`), or when `application.json` was not loaded. Reported at `/steps/{i}/operation`. |
| `AXC0151` | A process package operation step's `operation` names no operation in the manifest of the pinned version, matched ignoring letter case. Reported at `/steps/{i}/operation`. Its `input` and `output` are then not checked. |
| `AXC0160` | A schedule trigger's `schedule` is not a valid 5-field cron. Reported at `/schedule`. |
| `AXC0161` | A schedule trigger's `timeZone` is not an IANA time zone id known to the machine, or is written in another letter case. Reported at `/timeZone`. |
| `AXC0162` | A schedule trigger's `dataSource` names no loaded data source, matched ignoring letter case. Reported at `/dataSource`. |
| `AXC0163` | A schedule trigger's `dataSource` names a grouped data source. Reported at `/dataSource`. |
| `AXC0164` | A schedule trigger's `dataSource` is over another entity than the process's subject entity. Reported at `/dataSource`. |
| `AXC0165` | A key in a schedule trigger's `parameters` names no parameter of the data source, or repeats a parameter that an earlier key gives, ignoring letter case. Reported at `/parameters/{key}`. |
| `AXC0166` | A value in a schedule trigger's `parameters` is not a literal of its parameter's type, or is `null` for a required parameter. Reported at `/parameters/{key}`. |
| `AXC0167` | A schedule trigger does not give a parameter of its data source. Reported at `/parameters`, once for each missing parameter. |
| `AXC0170` | A migration's `entity` names no loaded entity, matched ignoring letter case. Not reported when the entity's file failed to load, or when a migration of that entity name holds `dropEntity`. Reported at `/entity`. |
| `AXC0171` | A `renameField`'s `from`, or a `dropField`'s or `convert`'s `field`, names no field of the entity as it is at that change. Reported at `/changes/{i}/from` or `/changes/{i}/field`. |
| `AXC0172` | A `renameField`'s `to` is already a field of the entity at that change. Reported at `/changes/{i}/to`. |
| `AXC0173` | A `fill`'s or `convert`'s `field` is not a field of the entity in the release. A child-collection field does not count. Reported at `/changes/{i}/field`. |
| `AXC0174` | An applied migration's file has changed since it was applied, or it is missing from the release. A changed file is reported at that file, and a missing one at `application.json`. Both have an empty path and the migration `id` as `resourceId`. Nothing is applied. A [rollback](authoring.md#rollback) to a release that lacks a migration applied after it gets the same code and is refused. |
| `AXC0176` | A pending `fill` leaves a `null` in some row, because its value is `null` for that row. Reported at `/changes/{i}/value` of the migration file, with the number of rows in the message. Nothing is applied. |
| `AXC0177` | A pending `convert` fails for a stored row, such as text that does not parse as the new type, a value that is too long, a required field left empty or a duplicate in a unique field. Also reported when an enum `convert` leaves a value that the field does not list. Reported at `/changes/{i}/value` of the migration file, with a fixed message and, for a database error, its PostgreSQL error code. Nothing is applied. |
| `AXC0178` | A pending `renameField`, `dropField`, `dropEntity` or `convert` changes a column that another release of the application uses, and `running` or `waiting` process instances are pinned to that release. One per migration and release, with the release id and the number of open instances in the message. Reported at the migration file with an empty path and the migration `id` as `resourceId`. Nothing is applied. Activate the release again once those instances end. |
| `AXC0180` | A role in a scenario's `users`, or an `assignee.role` of an `expectTask` step, names no declared role. Roles are matched exactly. Not reported when the role's file failed to load. Reported at `/users/{user}/{j}` or `/steps/{i}/assignee/role`. |
| `AXC0181` | A step's `as`, or an `assignee.user` of an `expectTask` step, names no user of the scenario. Reported at `/steps/{i}/as` or `/steps/{i}/assignee/user`. |
| `AXC0182` | A `createRecord` step's `entity` names no loaded entity, or names a child entity. Not reported when the entity's file failed to load. Reported at `/steps/{i}/entity`. |
| `AXC0183` | A key in a step's `values`, or in an item of a child collection, names no field of the entity. Reported at `/steps/{i}/values/{key}`. |
| `AXC0184` | A `startProcess` or `expectInstance` step's `process` names no loaded process, or a process over another entity than the record's. Not reported when the process's file failed to load. Reported at `/steps/{i}/process`. |
| `AXC0185` | An `expectInstance` step's `step` names no step of its process, or a `completeTask` or `expectTask` step's `step` names no task step of a process over the record's entity. Reported at `/steps/{i}/step`. |
| `AXC0186` | A `completeTask` step's `outcome` names no outcome of the task step. Reported at `/steps/{i}/outcome`. |
| `AXC0187` | A step's `record` is not saved by an earlier step. Reported at `/steps/{i}/record`. |
| `AXC0188` | A `createRecord` step's `save` is already used by an earlier step. Reported at `/steps/{i}/save`. |
| `AXC0189` | A value in a step's `values` does not fit its field's type: not a valid text, number, boolean, date, reference or listed enum value, or a child collection that is not an array of objects. Reported at `/steps/{i}/values/{key}`. |
| `AXC0190` | A stub names no loaded connector, no operation of the connector, no package of the application, or no operation of the pinned package. Reported at `/stubs/{i}/connector/name`, `/stubs/{i}/connector/operation` or `/stubs/{i}/operation`. |
| `AXC0191` | A key in a stub's `output` names no output field of the operation. Reported at `/stubs/{i}/output/{key}`. |
| `AXC0192` | A `processInspector` widget's `process` names no loaded process, ignoring letter case. Not reported when the process's file failed to load. Reported at `/widgets/{i}/process`. |
| `AXC0193` | An `advanceTime` step's `by` is not a positive duration in the form of a task's `dueIn`. Reported at `/steps/{i}/by`. |
| `AXC0194` | An `advanceTime` step in a scenario without `now`. Reported at `/steps/{i}`. |
| `AXC0200` | `module.json` is missing, unreadable, not JSON or fails its schema, or another file of the module version cannot be read. Reported at host start, at the failing pointer. A copy of a module in a stored release without a valid `modules/<id>/module.json` is reported the same way, when the release is rebuilt. |
| `AXC0201` | The folder names differ from the manifest `id` or `version`. Reported at host start, at `/id` or `/version`. In a stored release, the folder `modules/<id>/` differing from the manifest `id` is reported the same way. |
| `AXC0202` | A module file has a kind that a module may not hold, or no kind. Reported at compile, at `/kind` of `modules/<id>/<relative path>`. |
| `AXC0203` | A `modules` entry names a module with no installed version. Reported in `application.json` at `/modules/{i}/id`. |
| `AXC0204` | A `modules` entry has a version range in no valid form. Reported at `/modules/{i}/version`. |
| `AXC0205` | No installed version meets the range. The message lists the installed versions. Reported at `/modules/{i}/version`. |
| `AXC0206` | A module id appears twice in `modules`. Reported at `/modules/{i}/id` of each later entry. |
| `AXC0207` | A field's `target` or `sequence`, a data source's or form's `entity`, a widget's or policy grant's `dataSource`, or a qualified rule call `<module id>:<name>(…)` names a module that `modules` does not declare. Reported at `/fields/{i}/target`, `/fields/{i}/sequence`, `/entity`, `/widgets/{i}/dataSource`, `/grants/{i}/dataSource` or the expression's pointer. A reference under a declared module that could not be pinned is not reported again. |
| `AXC0208` | A field's `target` or `sequence` in a module entity, the `entity` of a module event, data source or form, or the `target` of a module data source parameter, contains `/`. Reported at that property in `modules/<id>/…`. |
| `AXC0209` | A module text key lacks the `<id>.` prefix. Reported at `/texts/{key}` of the module text. |
| `AXC0210` | An application locale has no text from a declared module. The message names the locale. Reported in `application.json` at `/modules/{i}`. |
| `AXC0211` | An application text key starts with the `<id>.` prefix of a declared module. Reported at `/texts/{key}`. |
| `AXC0212` | An application file sits under `modules/`, which releases reserve. Reported at that file, with an empty path. |
| `AXC0213` | The pinned version drops a module entity, drops one of its fields or changes a field's `type`, compared with the active release. The message names the module and both versions, and `none` when the module is removed. Reported at activation, at `/fields/{i}/type` of the module entity file in the release for a type change and at `/fields` for a dropped field. A dropped entity, or a removed module, is reported at the entity file of the active release, with an empty path. |
| `AXC0214` | A `migration`'s `entity` names a module entity, that is, it contains `/`. Reported at compile, at `/entity`. |
| `AXC0215` | The publisher stopped three times while running a publish or validate request. Reported on the request, with an empty file and path, see [lifecycle](authoring.md#lifecycle). |
| `AXC0216` | The main tenant has no preview tenant when a preview request runs, see [preview](authoring.md#preview). Reported on the request, with an empty file and path. |
| `AXC0217` | A `test` request runs and the publisher has no `Scenarios:ServerConnectionString`, see [test](authoring.md#test). The message names the setting. Reported on the request, with an empty file and path. |
| `AXC0218` | A `test` request names a scenario that the application does not have. The message names the scenario. Reported on the request, with an empty file and path, once for each unknown name. |
| `AXC0219` | A `test` request ran for more than 10 minutes, see [test](authoring.md#test). The message names the limit. Reported on the request, with an empty file and path. |
| `AXC0220` | The publisher stopped while a `test` request ran, see [test](authoring.md#test). Reported on the request, with an empty file and path. |
| `AXC0221` | A page's widget lists nest too deep: a `columns`, `tabs` or `section` widget would hold a list on level 5, and the page's own list is level 1. Reported at that container's pointer, such as `/widgets/0/columns/0/widgets/0/tabs/0/widgets/0/widgets/0`, see [resource files](resource-files.md). |
| `AXC0222` | The `width` of the columns of one `columns` widget do not add up to 12. The message gives the sum. Reported at `/widgets/{i}/columns`, or at that pointer inside a container. |
| `AXC0223` | A page holds a second `table` widget, whatever it shows. The message names the first table's pointer. Reported at the second table. |
| `AXC0224` | A page holds a second `form`, `taskInbox` or `processInspector` widget, counted together. The message names the first one's pointer. Reported at the second widget. |
| `AXC0225` | A form condition reads a field the form does not list, see [form conditions and steps](resource-files.md#form-conditions-and-steps). Reported at the condition. |
| `AXC0226` | A form field has `visibleWhen` and its entity field is `required`, see [form conditions and steps](resource-files.md#form-conditions-and-steps). Reported at the field's `visibleWhen`. |
| `AXC0227` | A form section `name` is already used by an earlier section, ignoring letter case. Reported at the repeated `name`. |
| `AXC0228` | A form step lists a section name that no section of the form has. Reported at the name in the step's `sections`. |
| `AXC0229` | A form step lists a section that is already in a step. Reported at the repeated name in the step's `sections`. |
| `AXC0230` | A form has `steps` and a section has no name or is in no step. Reported at the section. |
| `AXC0231` | A page parameter `name` is reserved, already used by an earlier parameter, or also the name of a parameter of the page's table data source that the table leaves unbound, ignoring letter case. `page`, `pageSize` and `sort` are reserved. Reported at the parameter's `name`. |
| `AXC0232` | A `navigate` action names a `page` that no loaded page has. A page that failed to load is not reported again. Reported at the action's `page`. |
| `AXC0233` | A `navigate` action gives a parameter that its target page does not declare, or gives one twice, ignoring letter case. Reported at `/parameters/{name}` of the action. |
| `AXC0234` | A `navigate` action leaves out a required parameter of its target page. Reported at the action. |
| `AXC0235` | A `navigate` action sets `record`, but its target page holds no form widget, or the form is over another entity than `record` refers to. Reported at `/record` of the action. |
| `AXC0236` | A page whose widget is a form declares `parameters`. A navigate action opens the form's record with `record`. Reported at `/parameters`. |
| `AXC0237` | A `kpi` widget names a data source without an `aggregate`, or one whose `groupBy` is not empty, so that it has no single total row. Reported at the widget's `dataSource`. |
| `AXC0238` | A `kpi` widget names a `measure` or a `comparison.measure` that its data source's `aggregate` does not declare, ignoring letter case. Reported at that property. |
| `AXC0239` | A `detail` widget's `record` names no page parameter, or a parameter that is optional, not a `reference`, or whose `target` is not the widget's entity, ignoring letter case. Reported at `/widgets/{i}/record`. |
| `AXC0240` | A `chart` widget names a data source whose `aggregate` does not have exactly one `groupBy` field, or a `category` that is not that field, ignoring letter case. Reported at the widget's `dataSource` or `category`. |
| `AXC0241` | A `chart` widget names a measure that its data source's `aggregate` does not declare, or one that is not a number, or lists more than one measure on a `pie`. A measure is a number when it is a `count` or a `sum`, or a `min` or `max` over an integer or a decimal. Reported at the measure's `name`, or at `measures` for a `pie`. |
