# Expression language

The expression language holds the logic of an application's configuration: validations, named
rules, computed fields, field defaults, data source filters, process conditions, policy filters and
module contracts. This page gives its grammar, types, operators, null rules, functions, cost limits,
the subset that translates to SQL, and its diagnostics. Logic lives in one typed language, never in
code, for the reasons in [knowledge](../domain/knowledge.md#logic-in-configuration) and
[D16](../decisions/d16-expression-language.md).

The language has two back ends, which give the same result for every expression in the
[SQL subset](#sql-subset):

- **Interpreter.** It evaluates an expression against one record, in the server process or the
  worker.
- **SQL translation.** It turns an expression into a `WHERE` condition, so that PostgreSQL filters
  the rows. Only a subset of the language translates.

Each use is type-checked by the compiler and runs on one back end:

| Use                                                                                                                        | Runs on                                                                                                                                           |
| -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Entity [validations](resource-files.md#entity-logic) and [computed fields](resource-files.md#entity-logic), field defaults | The interpreter, when the record API writes a record                                                                                              |
| Named [rules](resource-files.md#resource-file-shape)                                                                       | The interpreter runs the rule's expression for a caller it evaluates, and a filter's SQL inlines it                                               |
| Data source [filters](data-sources.md#resource-shape) and measure filters                                                  | SQL, which the data source endpoint runs                                                                                                          |
| [Policy](roles-and-policies.md) filters                                                                                    | SQL, which the record API and the data source endpoint run                                                                                        |
| [Process](processes.md#compile-checks) start conditions, decision `when`, step values and data change trigger `when`       | The interpreter, against a stored record that the data module reads: the start endpoint runs a start condition, and the worker runs each decision |
| Navigate action values                                                                                                     | The browser, which follows a path through the record API on click, see [frontend](frontend.md)                                                    |
| Migration `fill` and `convert` values                                                                                      | Checked against the SQL subset, see [migrations](resource-files.md#resource-file-shape)                                                           |
| [Module contract](module-contracts.md) guards, core validations and effect values                                          | The interpreter                                                                                                                                   |

## Grammar

The grammar below is the whole syntax of an expression. The parser reports any text it does not
allow as a syntax error.

```ebnf
expression     = or ;
or             = and , { "or" , and } ;
and            = not , { "and" , not } ;
not            = "not" , not | comparison ;
comparison     = additive , [ compareOp , additive | "is" , [ "not" ] , "null"
               | "in" , "(" , item , { "," , item } , ")" ] ;
compareOp      = "==" | "!=" | "<" | "<=" | ">" | ">=" ;
item           = ( literal - "null" ) | "date" , "(" , text , ")"
               | "dateTime" , "(" , text , ")" ;
additive       = multiplicative , { ( "+" | "-" ) , multiplicative } ;
multiplicative = unary , { ( "*" | "/" ) , unary } ;
unary          = "-" , unary | path ;
path           = primary , { "." , name } ;
primary        = literal | call | name | "(" , expression , ")" ;
call           = [ qualifier ] , name , "(" , [ expression , { "," , expression } ] , ")" ;
qualifier      = moduleId , ":" ;
literal        = integer | decimal | text | "true" | "false" | "null" ;
integer        = digit , { digit } ;
decimal        = digit , { digit } , "." , digit , { digit } ;
text           = "'" , { character - "'" | "''" } , "'" ;
name           = letter , { letter | digit } ;
moduleId       = letter , { letter | digit | "-" } ;
```

- **Whitespace.** Space, tab and line break between tokens are ignored.
- **Names.** A name is ASCII letters and digits that start with a letter, like a field name.
- **Qualified call.** A call to a rule of a [module](modules-and-templates.md#modules) is written
  `organization:IsDelegating(from, until)`: the module id, a `:` and the rule name, with no space
  around the `:`. The reader takes the letters, digits and `-` that run into the `:` as the module
  id, so `a-b:c(x)` calls the rule `c` of the module `a-b`, and subtracting the result of a call
  needs spaces: `a - b:c(x)`. Only a `:` makes a qualified call, so `total/ratio(x)` is a division.
  A qualified name must be called: `organization:IsDelegating` alone is `AXC0035`.
- **Keywords.** `and`, `or`, `not`, `is`, `in`, `null`, `true` and `false` are reserved in any
  letter case, because names ignore letter case and a field named `Null` would otherwise be
  ambiguous.
- **No chaining.** Comparisons do not chain, so `a < b < c` is a syntax error. Write
  `a < b and b < c`.
- **`in` list.** An item is a literal other than `null`, or a `date('…')` or `dateTime('…')` call
  with a text literal argument. The list cannot be empty, and an item that is not a constant is a
  syntax error.
- **Nothing else.** There are no loops, variables, assignment, comments or inline code, as
  [D6](../decisions/d6-in-configuration-logic-uses-a-typed-expression-language.md) chose.

## Types

Every expression has a type, which the compiler finds before the application is published. A field
gives the expression type of its field type:

| Field type   | Expression type | Notes                                                                                                                                                                                                                                  |
| ------------ | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `text`       | `text`          | Unicode text.                                                                                                                                                                                                                          |
| `integer`    | `integer`       | Signed 64-bit.                                                                                                                                                                                                                         |
| `decimal`    | `decimal`       | Exact, held as a .NET `decimal`: up to 28 digits after the point and a mantissa below 2^96. A value it cannot hold exactly is a [run-time error](#run-time-errors), never rounded. Only division and the functions that say so round. |
| `boolean`    | `boolean`       |                                                                                                                                                                                                                                        |
| `date`       | `date`          | A calendar date without a time zone.                                                                                                                                                                                                   |
| `date-time`  | `date-time`     | A UTC instant, to the microsecond.                                                                                                                                                                                                     |
| `enum`       | `enum`          | One value of one field's `values`.                                                                                                                                                                                                     |
| `reference`  | `reference`     | A record of the target entity.                                                                                                                                                                                                         |
| `money`      | `decimal`       | The amount. The currency is not part of the value.                                                                                                                                                                                     |
| `file`       | `file`          | Only `is null` and `is not null` accept it.                                                                                                                                                                                            |
| `references` | `list<Entity>`  | The set of target records. Only aggregates accept it.                                                                                                                                                                                  |

Two more types exist only inside expressions:

- **`null`.** The type of the literal `null`. It fits any type.
- **`list<Entity>`.** The type of a child collection field and of a `references` field. Only
  [aggregates](#aggregates) accept it, as their first argument, and anywhere else it is a type
  diagnostic. Over a `references` field, names in the item expression are the target's own fields,
  computed ones included. Paths through reference fields in an item expression resolve only in a
  data source filter and a policy filter, where they become SQL. Everywhere else the interpreter
  would need record reads for them, so a path in an item expression is an unknown name.

The types combine by these rules:

- **Widening.** An integer widens to a decimal when it is mixed with one. No other conversion is
  implicit, and text never becomes a number or a date.
- **Enums.** Two enum values compare only when both come from the same field's value set, or when
  one side is a text literal. A data source parameter of type `enum` also compares with an enum
  field when every value in the parameter's `values` is one of the field's `values`. A text literal
  compared with an enum must be one of the field's `values`, compared ordinally. Otherwise it is a
  type error at compile time.
- **Result type.** Each use sets the type its expression must have. Validation, rule and filter
  expressions need `boolean`, and a computed field needs its field's type. The result fits under the
  rules of `==`, except that a decimal never narrows to an integer. So an integer meets an expected
  decimal, `null` meets any type, and a listed text literal meets an expected enum.

## Operators and precedence

The operators, from lowest to highest precedence, are these:

| Level | Operators                                                 | Notes                                                    |
| ----- | --------------------------------------------------------- | -------------------------------------------------------- |
| 1     | `or`                                                      | Left to right.                                           |
| 2     | `and`                                                     | Left to right.                                           |
| 3     | `not`                                                     | Prefix.                                                  |
| 4     | `==` `!=` `<` `<=` `>` `>=` `is null` `is not null` `in` | Non-associative.                                         |
| 5     | `+` `-`                                                   | Left to right.                                           |
| 6     | `*` `/`                                                   | Left to right.                                           |
| 7     | unary `-`                                                 | Prefix.                                                  |
| 8     | `.`                                                       | Path. See [Names and references](#names-and-references). |

Each operator takes and gives these types:

- **Arithmetic.** `+`, `-` and `*` on two integers give an integer, and with a decimal on either
  side they give a decimal. They are exact.
- **Division.** `/` always gives a decimal, rounded half away from zero to 20 digits after the
  point. When the whole-number part leaves no room for 20 digits, it rounds to as many digits as
  fit. Only a whole-number part that does not fit at all is a [run-time error](#run-time-errors).
  Dividing by zero is a run-time error too.
- **Text.** `+` does not join text. Use `concat(...)`.
- **Ordering.** `<`, `<=`, `>` and `>=` work only on integers, decimals, dates and date-times.
  Text cannot be ordered, because .NET orders text by code unit and PostgreSQL by the database
  collation, so the two back ends would differ. Booleans, enums and references cannot be ordered
  either.
- **Equality.** `==` and `!=` work on any two values of the same type, including references and
  `null`. A reference compares by the record it points to.
- **Booleans.** `and`, `or`, `not` and the condition of `if` need `boolean`.
- **Membership.** `x in (a, b, …)` gives a boolean, which is true when `x == item` for some item.
  Every item must fit the type of `x` under the same rules as `==`, so an integer widens to a
  decimal. For an enum, each text item must be one of the field's `values`, checked at compile
  time. A wrong item type or an unknown enum value is a type diagnostic. `in` does not chain, and it
  sits at level 4, so `not x in ('a', 'b')` means `not (x in ('a', 'b'))`. There is no `not in`
  operator. Each item counts as a syntax node.

For example:

```text
status in ('submitted', 'approved')
```

## Null semantics

An empty field is `null`. These rules decide what `null` does in operators and in
[functions](#functions).

- **Propagation.** Arithmetic, ordering comparisons and most functions return `null` when an
  operand is `null`, and the function table says where a function differs. The `null` check comes
  before any error check, so `null / 0` is `null`, not a division by zero.
- **Equality is null-safe.** `null == null` is true. `x == null` and `x != null` are valid and are
  not type errors, and `is null` and `is not null` are a second way to write the same test.
  Equality is null-safe so that `status != previousStatus` is true when `previousStatus` is empty,
  because authors do not expect it to be unknown.
- **`in` is null-safe.** When `x` is `null`, `x in (…)` is `false`, never `null`. So
  `not (x in (…))` is true for an empty `x`.
- **Three-valued logic.** `and`, `or` and `not` treat `null` as unknown, and `not null` is `null`:

  | `a`   | `b`  | `a and b` | `a or b` |
  | ----- | ---- | --------- | -------- |
  | true  | null | null      | true     |
  | false | null | false     | null     |
  | null  | null | null      | null     |

- **Short circuit.** `and` and `or` evaluate from left to right and stop as soon as the left side
  decides the result. `false and x` and `true or x` do not evaluate `x`, so `x` costs no steps and
  raises no errors. This allows guards such as `count == 0 or total / count > 5`.
- **Null counts as false at the end.** Wherever the final boolean is used, validation included,
  `null` counts as false. So `quantity > 0` fails when `quantity` is empty, which differs from a
  PostgreSQL `CHECK` constraint. To allow an empty field, write:

  ```text
  quantity is null or quantity > 0
  ```

- **`if`.** A `null` condition picks the else branch. Only the picked branch is evaluated, so
  `if(count == 0, 0, total / count)` is a safe guard.
- **`coalesce`.** It returns its first argument that is not `null`, and the arguments after it are
  not evaluated.
- **`concat`.** It treats `null` as empty text.

## Literals

A literal writes a constant value:

| Form      | Example                            | Notes                                                                                                                                                                            |
| --------- | ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Integer   | `42`                               |                                                                                                                                                                                  |
| Decimal   | `12.50`                            | No exponent. Kept exactly.                                                                                                                                                       |
| Text      | `'submitted'`                      | Single quotes. `''` stands for one quote, as in `'it''s'`.                                                                                                                       |
| Boolean   | `true`, `false`                    |                                                                                                                                                                                  |
| Null      | `null`                             |                                                                                                                                                                                  |
| Date      | `date('2026-10-08')`               | `yyyy-MM-dd`.                                                                                                                                                                    |
| Date-time | `dateTime('2026-10-08T09:30:00Z')` | The RFC 3339 rules of the record API: an offset is required, and the fraction has up to 6 digits. See [request bodies and values](record-api.md#request-bodies-and-values). |

- **Date and date-time.** The argument of `date` and `dateTime` must be one text literal, and the
  type checker parses it with the rules in the table. So `date('2026-13-45')` is `AXC0047` at
  compile time in every use, reported at the call, such as
  `'2026-13-45' is not a valid date at character 7.`
- **Enum values.** An enum value is a text literal, checked against the field's `values`. See
  [Types](#types).
- **Negative numbers.** A negative number is unary minus applied to a literal.
- **Current time.** `now()`, `today()` and `toDate(date-time)` give the current instant and
  calendar dates, see [Functions](#functions).

## Names and references

A name in an expression is a field, a parameter, a collection or a rule, and what it can name
depends on where the expression is used. The type checker resolves names against the fields,
parameters and collections of the use, paths against the targets of reference fields where the use
allows paths, and calls against the rules it may call, ignoring letter case.

- **Letter case.** Names match ignoring letter case. This includes field, rule and function names.
- **Bare name.** A bare name is a field of the current record. In a data source filter it can also
  be a data source parameter, and the data source check reports a parameter and a field that share
  a name.
- **Path.** A path such as `department.name` follows a reference field to a field of the target
  record. A path may take at most 3 hops, and a `null` reference along the path makes the result
  `null`. The first name of a path is a field, never a data source parameter, because a reference
  parameter holds an id and not a record. Data source filters, policy filters, process expressions
  and navigate actions resolve paths. A validation, a computed field and a field default do not, so
  a path there is `AXC0046`.
- **Child collection.** A child collection field gives a `list<Entity>`, which only aggregates
  accept. Inside an aggregate's item expression, names are the child row's fields, computed ones
  included, and nothing else: no field of the owner, no collection and no rule. A `references`
  field gives a `list<Target>` the same way, with the target's fields in the item expression. In a
  data source filter and a policy filter, the item expression also resolves paths through the
  reference fields of the child row or the target, such as `any(lines, product.name == 'Desk')`.
- **Rule call.** A named rule is called like a function, such as `isLargeRequest(total)`. Arguments
  are checked against the rule's typed parameters: an integer fits a decimal parameter, and `null`
  fits any parameter. A rule body sees only its declared parameters. Parameter and result types are
  the scalar field types except `enum`, see [Resource file shape](resource-files.md#resource-file-shape).
  A rule name may not reuse a built-in function or aggregate name. A validation, a field default, a
  data source filter, a process expression and a policy filter can call rules, but not inside an
  aggregate's item expression, and a computed field cannot call them. A filter's SQL inlines the
  rule's body, see [SQL subset](#sql-subset).
- **Qualified rule call.** A rule of a [module](modules-and-templates.md#modules) that the
  application declares is called as `organization:IsDelegating(from, until)`, wherever a rule call
  is allowed: a rule body, a validation, a field default, a data source filter, a process
  expression and a policy filter. The module id must match exactly, and the rule name ignores letter
  case. A bare name in an application expression never reaches a module rule, and a qualified name
  never reaches an application rule or a built-in function. The interpreter runs the module rule's
  body, and a filter's SQL inlines it, with the module rules it calls in turn.
  - A module that `modules` in `application.json` does not declare is `AXC0207`.
  - A declared module without that rule is `AXC0050`, and so is a qualified call where no rules are
    callable, such as a computed field.
  - A module's rule bodies, entity validations and field defaults call that module's rules by bare
    name, and the rules of the modules its `module.json` declares as `beta:IsValid(x)`. A module
    data source filter calls only those qualified rules. In a module file, a qualified call to a
    module it does not declare, or with its own id, is `AXC0050`, not `AXC0207`.

Each use has its own scope:

- **Scope in a validation.** The entity's fields, computed ones included, its child collections and
  `references` fields through aggregates, and the named rules at its top level.
- **Scope in a computed field.** The entity's own fields that are not computed, and its child
  collections through aggregates. Another computed field, itself included, is an unknown name, and
  so is a `references` field. Inside an item expression, names are the child row's fields, computed
  ones included, and no rules. It cannot call `now()`, `today()` or `toDate`, which is `AXC0086`.
- **Scope in a field default.** No fields, no collections and no paths, because no record exists
  yet, so naming a field is an unknown name. It has the named rules and may call `now()`. Its type
  must fit the field, so a `date` field needs `toDate(now())`.
- **Scope in a data source filter.** The entity's fields and the data source parameters as plain
  names, and paths through reference fields. It can call the named rules, with fields, paths,
  parameters and literals as arguments. Its child collections and `references` fields type-check in
  aggregates, but only `any` and `count` are in the [SQL subset](#sql-subset), so another aggregate
  is `AXC0053`. Inside an aggregate's item expression, names are the child row's or the target's
  fields, and paths through their reference fields resolve. A measure `filter` of a
  [grouped data source](data-sources.md#aggregates) has the same scope.
- **Scope in a policy filter.** The entity's fields, paths through reference fields of up to 3
  hops, the named rules and [`currentUser()`](#current-user). The compiler type-checks it as a
  boolean and translates it to SQL, and it must stay inside the [SQL subset](#sql-subset), because
  record lists, single-record checks and [data sources](data-sources.md#authorization) run it as
  SQL. So it can use `any` and `count` over a child collection or a `references` field, such as
  `any(assignees, userId == currentUser())`, but not another aggregate or `lower`, which are
  `AXC0053`. A rule body cannot call `currentUser()`, so a filter passes the user id to a rule as an
  argument, such as `IsRequester(requester, currentUser())`.
- **Scope in a process expression.** A `startCondition`, a decision `when`, an `updateRecord` value,
  a task's `assignee.user` and a data change trigger's `when` see the subject record's fields,
  computed ones included, its child collections through aggregates, the named rules, and paths
  through reference fields of up to 3 hops. The compiler type-checks a condition as a boolean, an
  `updateRecord` value against its field's type and `assignee.user` as `text`.
  - The scope also has `id`, the subject record's id, typed as a reference parameter to the subject
    entity, as in a navigate action. So `id == manager` compares with a reference field that points
    to the subject entity, and a path cannot start at it: `id.name` is `AXC0047`. `id` comes first,
    so it wins over a field named `id`.
  - The data module reads the records a path names on the caller's connection and transaction
    before it evaluates, and reads each record at most once per evaluation. A reference that names
    no record gives `null`, as a `null` reference does. A stored decimal on any record read that
    .NET `decimal` cannot hold exactly stops the evaluation with a run-time error.
  - An aggregate may also name a collection of a referenced record by a path, such as
    `count(type.steps)`, and the data module then reads that record's collections and `references`
    sets once, see
    [collections through a path](#collections-through-a-path-and-the-outer-record).
  - A process without a subject sees its parameters and the named rules instead. See
    [processes](processes.md#steps) and [process flow](process-flow.md).
- **Scope in a navigate action.** On a table row, bare names are the row's columns, or the record's
  fields for a table over an entity, and on a form and a detail they are the record's fields. In a
  table's toolbar there are no names. The scope has no rules and no aggregates.
  - `id` is the id of the root record, typed as a reference parameter to the root entity, so a path
    cannot start at it: `id.name` is `AXC0047`.
  - A path such as `department.name` resolves in the parameters and `record` of an action on a
    table over an entity, a form and a detail, takes at most 3 hops, and has the type of the field
    it ends at. An unknown field on the path is `AXC0046`, and a path of 4 hops is `AXC0058`.
  - A path stays `AXC0046` in a row action's `visibleWhen` and in an action on a table over a data
    source, whose columns hold no reference to follow.

## Functions

The built-in functions below are called like rules. Function names ignore letter case. In the
signatures, `n` is an integer or a decimal. Unless the table says otherwise, a function returns
`null` when an argument is `null`. The SQL column says whether the function is in the
[SQL subset](#sql-subset).

| Function                    | Result                | Notes                                                                                                                                                                                                                                                                        | SQL |
| --------------------------- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --- |
| `length(text)`              | integer               | Counts Unicode code points.                                                                                                                                                                                                                                                  | Yes |
| `contains(text, text)`      | boolean               | Case-sensitive, ordinal.                                                                                                                                                                                                                                                     | Yes |
| `startsWith(text, text)`    | boolean               | Case-sensitive, ordinal.                                                                                                                                                                                                                                                     | Yes |
| `endsWith(text, text)`      | boolean               | Case-sensitive, ordinal.                                                                                                                                                                                                                                                     | Yes |
| `concat(text, …)`           | text                  | Treats `null` as empty. Never returns `null`.                                                                                                                                                                                                                                | Yes |
| `lower(text)`               | text                  |                                                                                                                                                                                                                                                                              | No  |
| `upper(text)`               | text                  |                                                                                                                                                                                                                                                                              | No  |
| `trim(text)`                | text                  |                                                                                                                                                                                                                                                                              | No  |
| `abs(n)`                    | same as `n`           |                                                                                                                                                                                                                                                                              | Yes |
| `round(decimal, integer)`   | decimal               | Rounds half away from zero to the given number of digits after the point. An integer first argument widens to a decimal. A digit count outside 0 to 28 is a [run-time error](#run-time-errors).                                                                            | Yes |
| `floor(n)`                  | same as `n`           |                                                                                                                                                                                                                                                                              | Yes |
| `ceiling(n)`                | same as `n`           |                                                                                                                                                                                                                                                                              | Yes |
| `year(date)`                | integer               |                                                                                                                                                                                                                                                                              | Yes |
| `month(date)`               | integer               | 1 to 12.                                                                                                                                                                                                                                                                     | Yes |
| `day(date)`                 | integer               | 1 to 31.                                                                                                                                                                                                                                                                     | Yes |
| `addDays(date, integer)`    | date                  | A result outside 0001 to 9999 is a [run-time error](#run-time-errors).                                                                                                                                                                                                       | Yes |
| `daysBetween(date, date)`   | integer               | Second minus first.                                                                                                                                                                                                                                                          | Yes |
| `coalesce(a, b, …)`         | type of the arguments | First argument that is not `null`. The arguments after it are not evaluated. All arguments fit each other under the rules of `==`. An integer and a decimal mix to a decimal, so a picked integer comes back as a decimal. A listed text literal and an enum mix to the enum. | Yes |
| `if(condition, then, else)` | type of the branches  | A `null` condition picks `else`. Only the picked branch is evaluated. The branches fit each other under the rules of `==`. An integer and a decimal mix to a decimal, so a picked integer comes back as a decimal.                                                           | Yes |
| `date(text literal)`        | date                  | See [Literals](#literals).                                                                                                                                                                                                                                                   | Yes |
| `dateTime(text literal)`    | date-time             | See [Literals](#literals).                                                                                                                                                                                                                                                   | Yes |
| `now()`                     | date-time             | The start time of the current transaction. Not allowed in a computed field (`AXC0086`).                                                                                                                                                                                      | Yes |
| `today()`                   | date                  | The calendar date of the transaction's time in the application's time zone. Not allowed in a computed field (`AXC0086`).                                                                                                                                                     | Yes |
| `toDate(date-time)`         | date                  | The calendar date of an instant in the application's time zone. `null` gives `null`. Not allowed in a computed field (`AXC0086`).                                                                                                                                            | Yes |
| `currentUser()`             | text                  | The signed-in user's id. Allowed only in a policy filter and in a data source `filter` (`AXC0095`). See [Current user](#current-user).                                                                                                                                       | Yes |
| `previous(field)`           | type of the field     | The field's value before the write, `null` on a create. Allowed only in a module contract (`AXC0336`), and its argument must be an own field (`AXC0337`). See [previous values](module-contracts.md#previous-values).                                                        | No  |
| `accountableUser()`         | text                  | The user who answers for the write, or `null`. Allowed only in a module contract's guard, core validation and effect value and in the `set` of an `updateRecord` or `createRecord` step (`AXC0341`). See [the accountable user](module-contracts.md#the-accountable-user). | No  |
| `decisionTask()`            | text                  | The id of the task whose completion the write follows, as a lowercase GUID with hyphens, or `null`. Allowed where `accountableUser()` is (`AXC0341`). See [the deciding task](module-contracts.md#the-deciding-task).                                                       | No  |

- **Dates.** The date functions work on `date` only, not on `date-time`. Taking a calendar day from
  an instant needs a time zone, so `toDate` does it.
- **Text.** The text functions take `text` only. An enum value is not text to them, because no
  conversion is implicit except integer to decimal.
- **The application time zone.** `today()` and `toDate(date-time)` use the
  [application time zone](resource-files.md#application-time-zone), which is `UTC` when the
  application names none. The time zone is part of the release, so a change applies to work that
  runs on the new release. The SQL translation converts with `AT TIME ZONE` and the time zone id as
  a bound parameter, so the interpreter and SQL agree. For example, at 18:00 UTC on 31 December
  both give 1 January of the next year in `Asia/Ho_Chi_Minh` and 31 December in `UTC`.
- **The current time.** `now()` gives the start time of the current transaction. It is an instant,
  so it never depends on a time zone. Every call in one transaction gives the same instant, and
  `today()` takes its instant the same way, so the interpreter and the SQL translation agree. The
  transaction is the following:

  | Use                | Transaction                                                                                                                                                   |
  | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
  | Process step       | The step's transaction, covering its decision branches, its `updateRecord` values and a task's `assignee.user`, the same instant as the step history's `started_at` |
  | Start condition    | The start transaction                                                                                                                                         |
  | Validation         | The transaction that writes the record                                                                                                                        |
  | Data source filter | The query, as PostgreSQL `now()`. A test scenario that sets `now` gives the PostgreSQL setting `axis.now`, which the filter reads first                       |
  | Rule               | The transaction of whatever calls it                                                                                                                          |

- **No current time in a computed field.** A computed field's value is stored and would go stale,
  so `now()`, `today()` or `toDate` in a computed field, including inside an aggregate's item
  expression, is `AXC0086` at the call.

### Current user

`currentUser()` gives the signed-in user's id as `text`, as the
[session](security.md#sign-in-and-the-session) holds it, so that a filter can compare a record with
the user, such as `requester == currentUser()` or `department.manager == currentUser()`.

- **Where.** It is allowed only in a [policy filter](roles-and-policies.md), including as an
  argument of a rule call there, and in a [data source](data-sources.md#resource-shape) `filter`.
  Anywhere else, including a measure `filter` and a rule body, it is `AXC0095`. A schedule trigger
  over a data source that calls it is `AXC0363`.
- **SQL only.** Every expression that may call it runs as SQL, so the interpreter does not evaluate
  it. It becomes the named parameter `@currentUser`, which the record API and the data source
  endpoint bind as `text`. The id is never spliced into the SQL text.
- **One value.** Every call in one request gives the same id.

### Aggregates

An aggregate works on a child collection or a `references` field. The first argument names the
field, or, in a process expression, is a path to the field of a referenced record, as in
[collections through a path](#collections-through-a-path-and-the-outer-record). The second
argument is an item expression, evaluated once per row. Inside it, names refer only to the child
row's or the target record's fields, computed ones included, and `outer.<field>` reads a field of
the [outer record](#collections-through-a-path-and-the-outer-record). Computed fields can use
aggregates over child collections. Validations and process expressions can use them over child
collections and `references` fields, and only process expressions over a path to a collection.

| Function                       | Result         | Empty list |
| ------------------------------ | -------------- | ---------- |
| `count(list)`                  | integer        | 0          |
| `count(list, condition)`       | integer        | 0          |
| `sum(list, item)`              | type of `item` | 0          |
| `avg(list, item)`              | decimal        | `null`     |
| `min(list, item)`              | type of `item` | `null`     |
| `max(list, item)`              | type of `item` | `null`     |
| `any(list, condition)`         | boolean        | false      |
| `all(list, condition)`         | boolean        | true       |
| `first(list, condition, item)` | type of `item` | `null`     |

For example, `sum(lines, quantity * unitPrice)` adds up the line totals.

- **Item types.** `sum` takes integer or decimal items. `min` and `max` take integer, decimal, date
  and date-time items, because they order them. The condition of `count`, `any`, `all` and `first`
  is boolean.
- **Decimal results.** A `sum`, `min` or `max` over decimal items gives a decimal, so the 0 of an
  empty `sum` is a decimal 0.
- **Null items.** `sum`, `min` and `max` skip `null` items. So a list of only `null` items sums to 0
  and has no `min` or `max`.
- **Condition.** A `null` condition counts as false.
- **Every row.** `any` and `all` evaluate every row, even after the result is known, so a run-time
  error in any row is always reported. Put guards inside the item expression, where `and` and `or`
  short-circuit.
- **Errors.** A `sum` past the integer range, or one that a .NET `decimal` cannot hold exactly, is a
  [run-time error](#run-time-errors), and so is an `avg` whose total a .NET `decimal` cannot hold
  exactly.
- **`avg`.** It takes integer or decimal items, skips `null` items and is `null` when none is left.
  Its result is a decimal rounded to 6 digits after the point.
- **`first`.** The `item` of the first row, in the list's stored order, whose `condition` is true,
  or `null` when none is. The `item` may be a scalar field, an enum or a reference of the row, which
  gives the record's id, so a step can store the record a row names. The `condition` is evaluated on
  every row, but only the picked row's `item` is, so an error in another row's item is not raised.
- **SQL.** Only `any` and `count` are in the SQL subset, so a data source or policy filter can use
  them. The other aggregates, `first` included, are `AXC0053`, see [SQL subset](#sql-subset).

### Collections through a path and the outer record

A path to a collection and `outer` let a process read the rows of another record, such as the
approval steps of a request's type, which the
[organization and approvals modules](built-in-modules.md) need.

- **A path to a collection.** The first argument of an aggregate may be a path that ends at a child
  collection or a `references` field of a referenced record, such as `count(type.steps)` or
  `sum(type.steps, minAmount)`, with at most 3 hops. A longer path is `AXC0058`, as for a path to a
  scalar field.
  - It is allowed in process expressions: conditions, decision branches, `set`, assignees, inputs,
    notification values and `fields`, and in a data change trigger's `when` and a start condition,
    which have the same scope. A validation, a computed field and a field default refuse it as an
    unknown name (`AXC0046`).
  - The step reads the rows in its transaction. A `null` reference on the path, or one that names
    no record, gives the aggregate's empty-list result, such as 0 for `count`.
  - A path to a collection cannot read a sensitive field in a notification value. Rows are read
    without the record cipher, as the rows of a `references` field are, so a sensitive value in a
    row read through a path makes the read fail.
- **`outer`.** Inside an item expression, `outer.<field>` reads a field of the record the whole
  expression runs on, and `outer.<path>` follows its references, such as
  `count(type.steps, minAmount == null or outer.amount >= minAmount)`. It works wherever aggregates
  run in memory.
  - `outer` names the top-level record, also in a nested aggregate, and the word `outer` is not a
    hop. A path after it resolves only where a path does without `outer`.
  - `outer.<collection>` and a bare `outer` are type errors (`AXC0047`). Outside an item expression
    `outer` is `AXC0354`.
  - An entity field named `outer`, in any letter case, is `AXC0355`, but a parameter named `outer`
    still works as a bare name.
  - A sensitive field or an unlisted form field is refused through `outer` too.
- **Not SQL.** A path to a collection and `first` are evaluated in memory, in the server and the
  worker, so a data source filter or a policy filter that uses either is `AXC0053`, and so is
  `outer` inside `any` or `count` in a filter.
- **Cost.** The rows count against the [cost bounds](#cost-bounds) as the rows of a child
  collection of the record do. Following the reference part of the path costs its usual steps, but
  the collection itself costs none, so `sum(r.lines, qty)` costs 1 step more than
  `sum(lines, qty)`.

## Cost bounds

Cost bounds keep every expression small enough to check and cheap enough to run. These limits are
checked at compile time, and passing one is a compile error:

| Limit                   | Value                                                | Diagnostic                                   |
| ----------------------- | ---------------------------------------------------- | -------------------------------------------- |
| Expression length       | 2,000 characters                                     | `AXC0036`                                    |
| Nesting depth           | 32                                                   | `AXC0037`                                    |
| Syntax nodes            | 500, counting each `in` item                         | `AXC0038`                                    |
| Inlined filter nodes    | 2,000, counting each inlined rule body once per call | `AXC0053`                                    |
| Reference hops per path | 3                                                    | `AXC0058`, and `AXC0043` in a projected path |
| Rule calls nested       | 8 deep                                               | `AXC0065`                                    |
| Rule call cycles        | None allowed                                         | `AXC0055`                                    |

Because no cycle is allowed, the evaluation of a rule call always ends.

- **Depth.** Depth is the height of the syntax tree. A name or literal has depth 1. Each operator,
  call, path step, `is null` and `in` adds one level above its highest operand, and each pair of
  parentheses also adds one level. So `-1` has depth 2, and `(a + b)` has depth 3.
- **Rule call depth.** A rule that calls no rule has depth 1, and each rule adds one level above its
  deepest callee. Only calls inside a rule body nest. A call given as an argument, such as
  `R(R(x))`, does not, because the inner call ends before the outer one starts. Every rule is
  measured, even one that nothing calls, so a validation is always within the limit. A chain past 8
  is reported once, at the lowest rule past the limit. A call to a module rule counts the module
  rule and every rule it calls, so a chain that passes from an application rule into module rules
  is measured as one.
- **Nodes.** Each literal, name, operator, call, `.name` path step, `is null` and `in` is one node.
  So each `in` item counts, and a `date('…')` item is two nodes: the call and its text. Parentheses
  are not nodes.
- **Inlined nodes.** A data source filter is counted again with the body of every rule it calls
  inlined, directly or through other rules. Each body counts once per call, and a call's arguments
  count once, however often the body uses them. The cap is checked before the SQL is built, see
  [SQL subset](#sql-subset).
- **Positions.** A diagnostic's offset is a zero-based index in UTF-16 characters, the same as a C#
  string index. Messages show it one-based, as "at character N". The
  [expression check](authoring.md#expression-check) route turns the offset into a range, the token
  at that offset.

At run time, the interpreter has these limits:

- **Step budget.** Each top-level evaluation has a budget of 10,000 steps. Each node evaluated is
  one step, and evaluation stops as soon as the budget runs out. The steps of a called rule count
  against the caller's budget.
  - An aggregate's item expression costs its steps once per row. The aggregate call is one step,
    and a collection argument that is a name costs none. A path to a collection costs the steps of
    its reference part, such as 1 step for `r` in `r.lines`. So `sum(lines, qty)` costs 1 step plus
    1 per row and runs out of budget at 10,000 rows, and `sum(r.lines, qty)` costs 2 steps plus 1
    per row and runs out at 9,999 rows.
  - A `concat` also costs one step for every full 1,000 characters of its result, counted before
    the text is built. So one evaluation builds at most about 10 million characters.
- **Long text.** The cost of text functions such as `contains` or `length` over very long field
  values is not bounded.
- **SQL.** The SQL translation has no step budget. The compile-time limits and the inlined filter
  cap bound its size.

## Run-time errors

A run-time error stops the evaluation and counts as a failure: a validation fails, a policy denies,
and a computed field rejects the write. In a data source filter, PostgreSQL raises the error and the
endpoint answers `400`. A run-time error is not a compile diagnostic. These are errors:

- division by zero;
- integer overflow;
- a decimal result that a .NET `decimal` cannot hold exactly;
- a date outside 0001 to 9999 from `addDays`;
- a `round` digit count outside 0 to 28;
- an exhausted step budget.

A `null` operand gives `null` before any error is checked.

## SQL subset

The SQL translation turns a filter into a parameterized `WHERE` condition, so the database filters
the rows and no value is ever spliced into the SQL text. It covers literals, bare field names,
field paths, data source parameters, every operator except `/`, the functions listed below, `any`
and `count`, and rule calls. Every operator is wrapped in parentheses, so the SQL keeps the
expression's precedence. These translate:

- literals, sent as named parameters `@f0`, `@f1`, … in order, each with the PostgreSQL type of its
  literal. The literals of the filter of measure `i` are `@m{i}_0`, `@m{i}_1`, … so they do not
  clash. `date('…')` and `dateTime('…')` are parsed at compile time and sent as a `date` and a
  `timestamptz`. The literal `null` is written as the keyword `NULL`, which is not a value;
- data source parameters, sent as named parameters `@p0`, `@p1`, … in declaration order, each with
  the PostgreSQL type of the parameter. A parameter that is not given is a typed `NULL` parameter,
  so it still compares with its column;
- field paths, as a column of a left-joined target table. Each distinct path is one left join on
  the target's `id`, so a `null` reference gives `NULL` and keeps the row;
- `==` and `!=`, as `IS NOT DISTINCT FROM` and `IS DISTINCT FROM`;
- `<`, `<=`, `>` and `>=`;
- `+`, `-`, `*` and unary `-`;
- `and`, `or` and `not`, because PostgreSQL is also three-valued;
- `is null` and `is not null`;
- `in`, as `(x IS NOT NULL AND x IN (@f0, @f1, …))`, with every item sent as a parameter;
- `if`, as `CASE WHEN … THEN … ELSE … END`;
- `coalesce` and `concat`;
- `length`, as `char_length`;
- `contains`, as `strpos(a, b) > 0`;
- `startsWith`, as `starts_with(a, b)`, and `endsWith`, as `right(a, char_length(b)) = b`, because
  PostgreSQL has no `ends_with`;
- `abs`;
- `floor`, `ceiling` and `round`, with the argument cast to `numeric` first, because PostgreSQL
  takes `floor` of a `bigint` as a floating-point number;
- `year`, `month` and `day`, through `extract`, cast to `bigint`;
- `addDays`, as `date + integer`;
- `daysBetween`, as `b - a`;
- `now()`, as the scenario's `axis.now` setting when it is set, and PostgreSQL `now()` otherwise,
  the start time of the query's transaction;
- `currentUser()`, as the named parameter `@currentUser`, which the caller binds as `text`;
- rule calls, inlined when the rule body is in the subset: the SQL holds the rule's body in place of
  the call, with each parameter replaced by its argument's SQL. Each argument is translated once,
  in the filter's context, so a literal argument is one parameter however often the body uses it.
  An argument for a `decimal` parameter, and the body of a rule with a `decimal` result, are cast to
  `numeric`, as the interpreter widens an integer to a decimal there. A rule that calls another rule
  is inlined the same way.

These are left out:

- **`/`.** The scale of a division result is set by the interpreter alone.
- **`lower` and `upper`.** Unicode case rules differ between .NET and PostgreSQL.
- **`trim`.** The two systems define whitespace differently.
- **Aggregates other than `any` and `count`.** A child collection has no column in the entity's
  table. A filter type-checks with the entity's child collections, so `sum`, `avg`, `min`, `max`,
  `all` and `first` in it are `AXC0053`.

`any(list, condition)`, `count(list)` and `count(list, condition)` translate when `list` is a child
collection or a `references` field of the filter's root entity:

- `any` becomes `EXISTS (SELECT 1 FROM <child or junction> WHERE <owner match> AND <condition>)`,
  and `count` a `(SELECT count(*) ...)` subquery of the same shape, so a row is never repeated.
- The condition is in the subset itself. Its names are the child row's fields, or for `references`
  the target's fields through a join on `target_id`, with paths through their references. A path is
  one scalar subquery per step, so it never adds a row. A condition outside the subset, such as one
  that calls `lower`, is `AXC0053` at that call.
- A `null` condition counts as false, as `WHERE` drops it.
- A filter that reads a computed field of the child row or the linked record while that field is
  being recomputed is refused, as one that reads a computed field of the record itself is.
- The indexes on `owner_id` and `target_id` serve these subqueries.

A filter follows these rules:

- **Not in the subset.** A filter that uses something left out is a compile diagnostic in the type
  family, `AXC0053`. When a rule's body uses something left out, such as `lower`, the filter is
  `AXC0053` at the call, and the message names the rule the filter calls. A call to a rule whose
  own expression has a diagnostic, or that is in a cycle, gets no further diagnostic, as in a
  validation.
- **Size.** The filter with every rule body inlined, counting each body once per call, may have at
  most 2,000 syntax nodes. Past that the filter is `AXC0053` at the call, naming the rule the filter
  calls. When the cap is passed by the filter's own nodes after a call, the message names the last
  rule the filter called.
- **Null rows.** `WHERE` drops rows where the condition is `null`, which matches `null` counting as
  false.
- **Parameters.** Values are always sent as parameters, never spliced into the SQL text.
- **Errors.** A [run-time error](#run-time-errors) in SQL, such as an integer overflow, fails the
  whole query, and the data source endpoint answers `400`. PostgreSQL does not short-circuit `and`
  and `or`, so a guard such as `quantity == 0 or quantity * 9223372036854775807 > 0` can still fail
  in SQL when it would not in the interpreter. The two back ends may differ only in such errors.
- **`round` digits.** A digit count outside 0 to 28 is a run-time error in the interpreter, but
  PostgreSQL accepts it.

## Writing expressions in resource files

An expression in a resource file is one JSON string. Entity validations, computed fields, rules,
field defaults, data source filters, [process](processes.md#resource-shape) conditions,
[policy](roles-and-policies.md) filters, and the `fill` and `convert` values of
[migrations](resource-files.md#resource-file-shape) are written this way:

```json
{
  "validation": "quantity is null or quantity > 0"
}
```

The property name above is only an example. For `expression` on a field, on a validation and on a
rule, see [Entity logic](resource-files.md#entity-logic), and for `filter` on a data source, see
[data sources](data-sources.md#resource-shape).

- **Quotes.** Text literals use single quotes, so they need no JSON escaping.
- **Long expressions.** A long expression can hold `\n` line breaks, because whitespace is ignored.
- **Size.** Logic past the 2,000-character limit is split into named rules.
- **One form.** There is no array-of-lines form.

## Diagnostics

The compiler reports a problem in an expression as a diagnostic, in one of three families:

- **Syntax.** Tokens, the grammar and literal forms.
- **Type.** Unknown names, type mismatches, unknown enum values, wrong argument counts, use of
  something outside the SQL subset in a filter, the current time in a computed field, and a
  function called where it is not allowed.
- **Cost.** The limits on length, depth, nodes, hops, rule call depth and rule call cycles.

Each diagnostic points at the JSON Pointer of the expression string, and its message gives the
character position inside the expression. Every code is in the
[diagnostic codes](diagnostic-codes.md) table.

- **`AXC0035`.** Every syntax error: an unknown character, a bad token, a literal that cannot be
  held exactly, or text the grammar does not allow. An integer above the signed 64-bit maximum and
  a decimal with more digits than .NET `decimal` holds are syntax errors.
- **`AXC0036`.** The expression is longer than 2,000 characters. It is not parsed.
- **`AXC0037`.** The expression is deeper than 32 levels.
- **`AXC0038`.** The expression has more than 500 syntax nodes.
- **`AXC0046`.** A name that is not in scope, named in the message. This includes an unknown field
  after a `.`, and a `.` path in a use that does not resolve paths, see
  [Names and references](#names-and-references).
- **`AXC0047`.** Operand types an operator, function or rule does not accept, at the operator or
  the call. The message names the types, and a collection as `list<Entity>`. This includes:
  - an `in` item that does not fit, two different enums, and an enum parameter with a value the
    field lacks;
  - a function or rule argument of the wrong type, and `coalesce` arguments or `if` branches that
    do not fit each other;
  - a `date` or `dateTime` call without a text literal argument, or with a text that is not a valid
    date or date-time;
  - a child collection or `references` field used anywhere but as the first argument of an
    aggregate, such as `lineItems == null` or `software == null`, reported at the name;
  - an aggregate whose first argument is not a child collection or a `references` field, such as
    `sum(title, amount)`, and an item of a type the aggregate does not accept;
  - a `.` after a field that is not a reference, after a data source parameter, or after anything
    that is not a field path, at the `.`.
- **`AXC0048`.** The expression's type does not fit the type its use needs. The message names the
  expected and the actual type.
- **`AXC0049`.** A text literal compared with an enum, or given where an enum is needed, is not one
  of the field's `values`. Reported at the literal.
- **`AXC0050`.** A call to a function or rule that does not exist, at the call. The message names
  it.
- **`AXC0207`.** A qualified rule call, such as `payroll:IsManager(x)`, names a module that
  `modules` in `application.json` does not declare, at the call. The same code covers a module
  named in a `target`, `sequence` or `entity`, see [Modules](modules-and-templates.md#modules).
- **`AXC0051`.** A function or rule call with the wrong number of arguments, at the call. This
  includes an aggregate without its item expression, such as `sum(lineItems)`. The message names the
  expected count, such as "needs 2 arguments" or "needs at least 1 argument", and the count found.
- **`AXC0053`.** A data source filter, a measure filter or a policy filter uses something outside
  the [SQL subset](#sql-subset), at the operator, call or path step. This includes `/`, `lower`,
  `upper`, `trim`, every aggregate except `any` and `count`, a call to a rule whose body is outside
  the subset, and a filter whose rule calls inline to more than 2,000 syntax nodes. The message
  names what is not translated, or the rule. It is reported only after the filter type-checks. A
  migration's `fill` or `convert` value is checked against the subset at `/changes/{i}/value`, even
  when its type check waits for activation, see [migrations](resource-files.md#resource-file-shape).
- **`AXC0058`.** A path takes more than 3 hops, at the `.` that goes past the limit.
- **`AXC0086`.** A computed field calls `now()`, `today()` or `toDate`, including inside an
  aggregate's item expression, at the call. A stored value cannot depend on the current time or the
  application's time zone.
- **`AXC0095`.** An expression that is not a policy filter or a data source `filter` calls
  `currentUser()`, at the call. This includes a validation, a computed field, a measure filter, a
  process expression and a rule body. A rule argument inside a policy filter may call it.
- **`AXC0336`.** An expression that is not a module contract's expression calls `previous`, at the
  call. This includes an entity validation, a computed field, a policy filter, a process expression
  and a rule body.
- **`AXC0337`.** The argument of `previous` is not the bare name of one of the entity's own fields,
  at the call. A child collection, a `references` field, an unknown name, any other expression, and
  a call inside an aggregate's item expression are all this.
- **`AXC0341`.** An expression that is not a module contract's guard, core validation or effect
  value, or the `set` of an `updateRecord` or `createRecord` step, calls `accountableUser()` or
  `decisionTask()`, at the call. This includes a decision condition, a computed field, a policy
  filter and a rule body.
- **`AXC0354`.** `outer` is used outside an aggregate's item expression, such as in a rule body, at
  the name or the `.` after it.
- **`AXC0055`.** Rules call each other in a cycle. It is reported on the rule file, not at a call,
  and names every rule in the cycle. See [the Check step](configuration.md).
- **`AXC0065`.** Rule calls nest more than 8 deep. It is reported once, at the rule file of the
  lowest rule past the limit, not at its callers. The message names the rules in the chain in call
  order. A rule in a cycle, or that calls into one, gets only `AXC0055`.
- **First problem only.** The parser and the type checker each stop at the first problem and
  report only that one. So one expression gives at most one diagnostic.
