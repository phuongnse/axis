# Expression language

Detailed reference for the expression language: grammar, types, operators,
null rules, functions, cost limits and the SQL subset. The grammar, the syntax
diagnostics and the length, depth and node limits are built in
`Axis.Expressions`. So are the type checker and the interpreter for literals,
bare field names, every operator, every [function](#functions) and the
[aggregates](#aggregates), and the translation of the [SQL subset](#sql-subset). Sections
marked *(planned for M2)* are not built yet. Entity
[validations](configuration.md#entity-logic) and
[computed fields](configuration.md#entity-logic) are resource file uses: the
compiler type-checks them and the record API evaluates them. A validation
can also call a named [rule](configuration.md#resource-file-shape): the
compiler checks the call and the rule, and the interpreter runs the rule's
expression. Data source
[filters](data-sources.md#resource-shape) are another: the compiler checks
and translates them, and the data source endpoint runs them as SQL. Other uses
come with the issues that build them. Dn
refers to
[decisions.md](../decisions.md). The language follows
[D6](../decisions.md#d6-in-configuration-logic-uses-a-typed-expression-language--agreed)
and [D16](../decisions.md#d16-expression-language--agreed). The reasons are in
[knowledge](../domain/knowledge.md#logic-in-configuration).

One language holds the logic of validation, named rules, computed fields and
data source filters. Later it also holds conditions, routing and policy
filters. It has two back ends:

- **Interpreter.** It evaluates an expression against one record, in the
  server process.
- **SQL translation.** It turns an expression into a `WHERE` condition for a
  data source filter. Only a subset of the language translates. See
  [SQL subset](#sql-subset).

Both back ends must give the same result for every expression in the subset.

## Grammar

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
call           = name , "(" , [ expression , { "," , expression } ] , ")" ;
literal        = integer | decimal | text | "true" | "false" | "null" ;
integer        = digit , { digit } ;
decimal        = digit , { digit } , "." , digit , { digit } ;
text           = "'" , { character - "'" | "''" } , "'" ;
name           = letter , { letter | digit } ;
```

- **Whitespace.** Space, tab and line break between tokens are ignored.
- **Names.** A name is ASCII letters and digits that start with a letter, like
  a field name.
- **Keywords.** `and`, `or`, `not`, `is`, `in`, `null`, `true` and `false` are
  reserved in any letter case. Names ignore letter case, so a field named
  `Null` would otherwise be ambiguous.
- **No chaining.** Comparisons do not chain, so `a < b < c` is a syntax
  error. Write `a < b and b < c`.
- **`in` list.** An item is a literal other than `null`, or a `date('…')` or
  `dateTime('…')` call with a text literal argument. The list cannot be empty.
  An item that is not a constant is a syntax error.
- **Nothing else.** There are no loops, variables, assignment, comments or
  inline code (D6).

## Types

Every expression has a type, found at compile time.

| Field type | Expression type | Notes |
| --- | --- | --- |
| `text` | `text` | Unicode text. |
| `integer` | `integer` | Signed 64-bit. |
| `decimal` | `decimal` | Exact, held as a .NET `decimal`: up to 28 digits after the point and a mantissa below 2^96. A value it cannot hold exactly is a [run-time error](#run-time-errors), never rounded. Only division and functions that say so round. |
| `boolean` | `boolean` | |
| `date` | `date` | A calendar date without a time zone. |
| `date-time` | `date-time` | A UTC instant, to the microsecond. |
| `enum` | `enum` | One value of one field's `values`. |
| `reference` | `reference` | A record of the target entity. |

Two more types exist only inside expressions:

- **`null`.** The type of the literal `null`. It fits any type.
- **`list<Entity>`.** The type of a child collection field. Only
  [aggregates](#aggregates) accept it, as their first argument. Anywhere
  else it is a type diagnostic.

Rules:

- **Widening.** An integer widens to a decimal when it is mixed with one. No
  other conversion is implicit. Text never becomes a number or a date.
- **Enums.** Two enum values compare only when both come from the same
  field's value set, or when one side is a text literal. A data source
  parameter of type `enum` also compares with an enum field when every value
  in the parameter's `values` is one of the field's `values`. A text literal
  compared with an enum must be one of the field's `values`, compared
  ordinally. Otherwise it is a type error at compile time.
- **Result type.** Each use sets the type its expression must have.
  Validation, rule and filter expressions need `boolean`. A computed field
  needs its field's type. The result fits under the rules of `==`, except
  that a decimal never narrows to an integer. So an integer meets an expected
  decimal, `null` meets any type, and a listed text literal meets an expected
  enum.

## Operators and precedence

From lowest to highest precedence:

| Level | Operators | Notes |
| --- | --- | --- |
| 1 | `or` | Left to right. |
| 2 | `and` | Left to right. |
| 3 | `not` | Prefix. |
| 4 | `==` `!=` `<` `<=` `>` `>=` `is null` `is not null` `in` | Non-associative. |
| 5 | `+` `-` | Left to right. |
| 6 | `*` `/` | Left to right. |
| 7 | unary `-` | Prefix. |
| 8 | `.` | Path. See [Names and references](#names-and-references). |

Typing rules:

- **Arithmetic.** `+`, `-` and `*` on two integers give an integer. With a
  decimal on either side they give a decimal. They are exact.
- **Division.** `/` always gives a decimal, rounded half away from zero to 20
  digits after the point. When the whole-number part leaves no room for 20
  digits, it rounds to as many digits as fit. Only a whole-number part that
  does not fit at all is a [run-time error](#run-time-errors). Dividing by
  zero is a run-time error too.
- **Text.** `+` does not join text. Use `concat(...)`.
- **Ordering.** `<`, `<=`, `>` and `>=` work only on integers, decimals,
  dates and date-times. Text cannot be ordered in v1, because .NET orders
  text by code unit and PostgreSQL orders it by the database collation. The
  two back ends would differ. Booleans, enums and references cannot be
  ordered either.
- **Equality.** `==` and `!=` work on any two values of the same type,
  including references and `null`. A reference compares by the record it
  points to.
- **Booleans.** `and`, `or`, `not` and the condition of `if` need `boolean`.
- **Membership.** `x in (a, b, …)` gives a boolean. It is true when `x == item`
  for some item. Every item must fit the type of `x`, under the same rules as
  `==`. An integer widens to a decimal. For an enum, each text item must be
  one of the field's `values`, checked at compile time. A wrong item type or
  an unknown enum value is a type diagnostic. `in` does not chain, and it
  sits at level 4, so `not x in ('a', 'b')` means `not (x in ('a', 'b'))`.
  There is no `not in` operator. Each item counts as a syntax node.

Example:

```text
status in ('submitted', 'approved')
```

## Null semantics

An empty field is `null`. These rules decide what `null` does in operators
and in [functions](#functions).

- **Propagation.** Arithmetic, ordering comparisons and most functions return
  `null` when an operand is `null`. The function table says where a function
  differs. The `null` check comes before any error check, so `null / 0` is
  `null`, not a division by zero.
- **Equality is null-safe.** `null == null` is true. `x == null` and
  `x != null` are valid and are not type errors. `is null` and `is not null`
  are a second way to write the same test. Equality is null-safe so that
  `status != previousStatus` is true when `previousStatus` is empty. Authors
  do not expect it to be unknown.
- **`in` is null-safe.** `x in (…)` is true when `x == item` for some item.
  When `x` is `null`, the result is `false`, never `null`. So
  `not (x in (…))` is true for an empty `x`.
- **Three-valued logic.** `and`, `or` and `not` treat `null` as unknown:

  | `a` | `b` | `a and b` | `a or b` |
  | --- | --- | --- | --- |
  | true | null | null | true |
  | false | null | false | null |
  | null | null | null | null |

  `not null` is `null`.

- **Short circuit.** `and` and `or` evaluate from left to right and stop as
  soon as the left side decides the result. `false and x` and `true or x` do
  not evaluate `x`, so `x` costs no steps and raises no errors. This allows
  guards such as `count == 0 or total / count > 5`.

- **Null counts as false at the end.** Wherever the final boolean is used,
  `null` counts as false. This includes validation. So `quantity > 0` fails
  when `quantity` is empty. To allow an empty field, write the idiom:

  ```text
  quantity is null or quantity > 0
  ```

  This differs from a PostgreSQL `CHECK` constraint, which passes when its
  condition is `null`.

- **`if`.** A `null` condition picks the else branch. Only the picked branch
  is evaluated, so `if(count == 0, 0, total / count)` is a safe guard.
- **`coalesce`.** It returns its first argument that is not `null`. The
  arguments after it are not evaluated.
- **`concat`.** It treats `null` as empty text.

## Literals

| Form | Example | Notes |
| --- | --- | --- |
| Integer | `42` | |
| Decimal | `12.50` | No exponent. Kept exactly. |
| Text | `'submitted'` | Single quotes. `''` stands for one quote, as in `'it''s'`. |
| Boolean | `true`, `false` | |
| Null | `null` | |
| Date | `date('2026-10-08')` | `yyyy-MM-dd`. |
| Date-time | `dateTime('2026-10-08T09:30:00Z')` | Same RFC 3339 rules as the record API: an offset is required, and the fraction has up to 6 digits. See [request bodies and values](record-api.md#request-bodies-and-values). |

- **Date and date-time.** The argument of `date` and `dateTime` must be one
  text literal. The type checker enforces this, but it does not check the
  text. In a data source filter, the SQL translation parses the text, so
  `date('2026-13-45')` is `AXC0053` at compile time. Elsewhere, such as in a
  validation, it passes the compiler and is a
  [run-time error](#run-time-errors) when it is evaluated. Checking the text
  at compile time for every use is *(planned for M2)*.
- **Enum values.** An enum value is a text literal, checked against the
  field's `values`. See [Types](#types).
- **Negative numbers.** A negative number is unary minus applied to a
  literal.
- **Current time.** v1 has no `now()` and no `today()`. They come later,
  together with a time zone rule.

## Names and references

*(planned for M2)*. Bare field names, data source parameters, paths in data
source filters, child collections in aggregates and rule calls from
validations are built: the type checker resolves names against the fields,
parameters and collections it is given, paths against the reference fields'
targets when it is given a way to find them, and calls against the rules it
is given, ignoring letter case. Paths in validations and computed fields are
not built, because the interpreter cannot read related records. There a path
is `AXC0046`.

- **Letter case.** Names match ignoring letter case. This includes field,
  rule and function names.
- **Bare name.** A bare name is a field of the current record. In a data
  source filter it can also be a data source parameter. When a parameter and
  a field share a name, the data source check reports it.
- **Path.** A path such as `department.name` follows a reference field to a
  field of the target record. A path may take at most 3 hops. A `null`
  reference along the path makes the result `null`. The first name of a path
  is a field, never a data source parameter, because a reference parameter
  holds an id and not a record.
- **Child collection.** A child collection field gives a `list<Entity>`,
  which only aggregates accept. Inside an aggregate's item expression, names
  are the child row's fields, computed ones included, and nothing else: no
  field of the owner, no collection and no rule.
- **Rule call.** A named rule is called like a function, such as
  `isLargeRequest(total)`. Arguments are checked against the rule's typed
  parameters: an integer fits a decimal parameter, and `null` fits any
  parameter. A rule body sees only its declared parameters. Parameter and
  result types are the scalar field types except `enum`, which comes later
  (see [Resource file shape](configuration.md#resource-file-shape)). A rule
  name may not reuse a built-in function or aggregate name. Only validations
  can call rules for now, and not inside an aggregate's item expression.
  Calls from computed fields and filters come later.
- **Scope in a computed field.** The expression sees the entity's own fields
  that are not computed and its child collections through aggregates, and no
  reference path. Another computed field, itself included, is an unknown
  name. Inside an item expression, names are the child row's fields,
  computed ones included, and no rules.
- **Scope in a validation.** The expression sees the entity's fields,
  computed ones included, its child collections through aggregates, and the
  named rules at its top level.
- **Scope in a data source filter.** A filter sees the entity's fields and the
  data source parameters as plain names, and paths through reference fields.
  It has no rules. Its child collections type-check in aggregates, but no
  aggregate is in the [SQL subset](#sql-subset), so one is `AXC0053`.

## Functions

Function names ignore letter case. In the signatures, `n` is an integer or a
decimal. Unless the table says otherwise, a function returns `null` when an
argument is `null`.

| Function | Result | Notes | SQL |
| --- | --- | --- | --- |
| `length(text)` | integer | Counts Unicode code points. | Yes |
| `contains(text, text)` | boolean | Case-sensitive, ordinal. | Yes |
| `startsWith(text, text)` | boolean | Case-sensitive, ordinal. | Yes |
| `endsWith(text, text)` | boolean | Case-sensitive, ordinal. | Yes |
| `concat(text, …)` | text | Treats `null` as empty. Never returns `null`. | Yes |
| `lower(text)` | text | | No |
| `upper(text)` | text | | No |
| `trim(text)` | text | | No |
| `abs(n)` | same as `n` | | Yes |
| `round(decimal, integer)` | decimal | Rounds half away from zero to the given number of digits after the point. An integer first argument widens to a decimal. A digit count outside 0 to 28 is a [run-time error](#run-time-errors). | Yes |
| `floor(n)` | same as `n` | | Yes |
| `ceiling(n)` | same as `n` | | Yes |
| `year(date)` | integer | | Yes |
| `month(date)` | integer | 1 to 12. | Yes |
| `day(date)` | integer | 1 to 31. | Yes |
| `addDays(date, integer)` | date | A result outside 0001 to 9999 is a [run-time error](#run-time-errors). | Yes |
| `daysBetween(date, date)` | integer | Second minus first. | Yes |
| `coalesce(a, b, …)` | type of the arguments | First argument that is not `null`. The arguments after it are not evaluated. All arguments fit each other under the rules of `==`. An integer and a decimal mix to a decimal, so a picked integer comes back as a decimal. A listed text literal and an enum mix to the enum. | Yes |
| `if(condition, then, else)` | type of the branches | A `null` condition picks `else`. Only the picked branch is evaluated. The branches fit each other under the rules of `==`. An integer and a decimal mix to a decimal, so a picked integer comes back as a decimal. | Yes |
| `date(text literal)` | date | See [Literals](#literals). | Yes |
| `dateTime(text literal)` | date-time | See [Literals](#literals). | Yes |

The date functions work on `date` only, not on `date-time`. Taking a calendar
day from an instant needs a time zone, and the time zone rule comes later.

The text functions take `text` only. An enum value is not text to them,
because no conversion is implicit except integer to decimal.

### Aggregates

An aggregate works on a child collection. The first argument names the
collection. The second argument is an item expression, evaluated once per
row. Inside it, names refer only to the child row's fields, computed ones
included. Computed fields and validations can use aggregates.

| Function | Result | Empty list |
| --- | --- | --- |
| `count(list)` | integer | 0 |
| `count(list, condition)` | integer | 0 |
| `sum(list, item)` | type of `item` | 0 |
| `min(list, item)` | type of `item` | `null` |
| `max(list, item)` | type of `item` | `null` |
| `any(list, condition)` | boolean | false |
| `all(list, condition)` | boolean | true |

- **Example.** `sum(lines, quantity * unitPrice)` adds up the line totals.
- **Item types.** `sum` takes integer or decimal items. `min` and `max` take
  integer, decimal, date and date-time items, because they order them. The
  condition of `count`, `any` and `all` is boolean.
- **Decimal results.** A `sum`, `min` or `max` over decimal items gives a
  decimal, so the 0 of an empty `sum` is a decimal 0.
- **Null items.** `sum`, `min` and `max` skip `null` items. So a list of
  only `null` items sums to 0 and has no `min` or `max`.
- **Condition.** A `null` condition counts as false.
- **Every row.** `any` and `all` evaluate every row, even after the result
  is known. So a run-time error in any row is always reported. Put guards
  inside the item expression, where `and` and `or` short-circuit.
- **Errors.** A `sum` past the integer range, or one that a .NET `decimal`
  cannot hold exactly, is a [run-time error](#run-time-errors).
- **SQL.** No aggregate is in the SQL subset.

## Cost bounds

Compile-time limits. A compile error is reported when one is exceeded:

| Limit | Value |
| --- | --- |
| Expression length | 2,000 characters |
| Nesting depth | 32 |
| Syntax nodes | 500, counting each `in` item |
| Reference hops per path | 3 |
| Rule calls nested | 8 deep |
| Rule call cycles | None allowed |

Every compile-time limit is built. More than 3 hops is `AXC0058` in a data
source filter and `AXC0043` in a projected `path`. Rule calls nested more
than 8 deep are `AXC0065`. A cycle is `AXC0055`, so evaluation of a rule call
always ends.

- **Depth.** Depth is the height of the syntax tree. A name or literal has
  depth 1. Each operator, call, path step, `is null` and `in` adds one level
  above its highest operand. Each pair of parentheses also adds one level. So
  `-1` has depth 2, and `(a + b)` has depth 3.
- **Rule call depth.** A rule that calls no rule has depth 1. Each rule adds
  one level above its deepest callee. Only calls inside a rule body nest. A
  call given as an argument, such as `R(R(x))`, does not, because the inner
  call ends before the outer one starts. Every rule is measured, even one
  that nothing calls, so a validation is always within the limit. A chain
  past 8 is reported once, at the lowest rule past the limit.
- **Nodes.** Each literal, name, operator, call, `.name` path step, `is null`
  and `in` is one node. So each `in` item counts, and a `date('…')` item is two
  nodes: the call and its text. Parentheses are not nodes.
- **Positions.** A diagnostic's offset is a zero-based index in UTF-16
  characters, the same as a C# string index. Messages show it one-based, as
  "at character N".

Run-time limit:

- **Step budget.** Each top-level evaluation has a budget of 10,000 steps.
  Each node evaluated is one step, and evaluation stops as soon as the budget
  runs out. An aggregate's item expression costs its steps once per row. The
  aggregate call is one step, and its collection argument costs none. So
  `sum(lines, qty)` costs 1 step plus 1 per row, and runs out of budget at
  10,000 rows. The steps of a called rule count against the caller's
  budget. A `concat` also costs one step for every full 1,000 characters of
  its result, counted before the text is built. So one evaluation builds at
  most about 10 million characters.
- **Long text.** The cost of text functions such as `contains` or `length`
  over very long field values is not bounded.
- **SQL.** The SQL translation has no step budget. The compile-time limits
  bound its size.

## Run-time errors

These are errors:

- division by zero;
- integer overflow;
- a decimal result that a .NET `decimal` cannot hold exactly;
- a `date` or `dateTime` literal that is not a valid date or date-time;
- a date outside 0001 to 9999 from `addDays`;
- a `round` digit count outside 0 to 28;
- an exhausted step budget.

A `null` operand gives `null` before any error is checked. An error stops the evaluation and counts as a failure. A validation fails, a
policy denies, and a computed field rejects the write. In a data source
filter, the error is raised by PostgreSQL and the endpoint answers `400`. A run-time error is not
a compile diagnostic.

## SQL subset

The SQL translation is built for literals, bare field names, field paths,
data source parameters, every operator except `/`, and every function the
list below names. Rule calls are *(planned for M2)*. Until they are built, a
rule call is `AXC0050`, because a filter cannot call rules yet.

These translate to SQL:

- literals, sent as named parameters `@f0`, `@f1`, … in order, each with the
  PostgreSQL type of its literal. `date('…')` and `dateTime('…')` are
  parsed at compile time and sent as a `date` and a `timestamptz`. The
  literal `null` is written as the keyword `NULL`, which is not a value;
- data source parameters, sent as named parameters `@p0`, `@p1`, … in
  declaration order, each with the PostgreSQL type of the parameter. A
  parameter that is not given is a typed `NULL` parameter, so it still
  compares with its column;
- field paths, as a column of a left-joined target table. Each distinct path
  is one left join on the target's `id`, so a `null` reference gives `NULL`
  and keeps the row;
- `==` and `!=`, as `IS NOT DISTINCT FROM` and `IS DISTINCT FROM`;
- `<`, `<=`, `>` and `>=`;
- `+`, `-`, `*` and unary `-`;
- `and`, `or` and `not`, because PostgreSQL is also three-valued;
- `is null` and `is not null`;
- `in`, as `(x IS NOT NULL AND x IN (@f0, @f1, …))`, with every item sent as
  a parameter;
- `if`, as `CASE WHEN … THEN … ELSE … END`;
- `coalesce` and `concat`;
- `length`, as `char_length`;
- `contains`, as `strpos(a, b) > 0`;
- `startsWith`, as `starts_with(a, b)`, and `endsWith`, as
  `right(a, char_length(b)) = b`, because PostgreSQL has no `ends_with`;
- `abs`;
- `floor`, `ceiling` and `round`, with the argument cast to `numeric` first,
  because PostgreSQL takes `floor` of a `bigint` as a floating-point number;
- `year`, `month` and `day`, through `extract`, cast to `bigint`;
- `addDays`, as `date + integer`;
- `daysBetween`, as `b - a`;
- rule calls, inlined when the rule body is in the subset *(planned for M2)*.

Every operator is wrapped in parentheses, so the SQL keeps the expression's
precedence.

These are left out:

- **`/`.** The scale of a division result is set by the interpreter alone.
- **`lower` and `upper`.** Unicode case rules differ between .NET and
  PostgreSQL.
- **`trim`.** The two systems define whitespace differently.
- **Aggregates.** A child collection has no column in the entity's table.
  A filter type-checks with the entity's child collections, so an aggregate
  in it is `AXC0053`.

Rules:

- **Not in the subset.** A filter that uses something left out is a compile
  diagnostic in the type family, `AXC0053`. So is a `date('…')` or
  `dateTime('…')` text that is not valid.
- **Null rows.** `WHERE` drops rows where the condition is `null`. This
  matches `null` counting as false.
- **Parameters.** Values are always sent as parameters. They are never
  spliced into the SQL text.
- **Errors.** A [run-time error](#run-time-errors) in SQL, such as an
  integer overflow, fails the whole query. The data source endpoint answers
  `400`. PostgreSQL does not short-circuit `and` and `or`, so a guard such as
  `quantity == 0 or quantity * 9223372036854775807 > 0` can still fail in SQL
  when it would not in the interpreter. The two back ends may differ only in
  such errors.
- **`round` digits.** A digit count outside 0 to 28 is a run-time error in
  the interpreter, but PostgreSQL accepts it.

## Writing expressions in resource files

Entity validations, computed fields, rules and data source filters are written this
way. The other uses are *(planned for M2)*.

An expression is one JSON string:

```json
{
  "validation": "quantity is null or quantity > 0"
}
```

The property name above is illustrative. The issue for each feature defines
the real property names. For `expression` on a field, on a validation and on a
rule, see [Entity logic](configuration.md#entity-logic). For `filter` on a
data source, see [data sources](data-sources.md#resource-shape).

- **Quotes.** Text literals use single quotes, so they need no JSON escaping.
- **Long expressions.** A long expression can hold `\n` line breaks, because
  whitespace is ignored.
- **Size.** Logic past the 2,000-character limit is split into named rules.
- **One form.** There is no array-of-lines form.

## Diagnostics

There are three families:

- **Syntax.** Tokens, the grammar and literal forms.
- **Type.** Unknown names, type mismatches, unknown enum values, wrong
  argument counts, and use of something outside the SQL subset in a filter.
- **Cost.** The limits on length, depth, nodes, hops, rule call depth and
  rule call cycles.

Each diagnostic points at the JSON Pointer of the expression string. Its
message gives the character position inside the expression. The `AXCnnnn`
codes are in the [diagnostic table](configuration.md#configuration-pipeline):

- **`AXC0035`.** Every syntax error: an unknown character, a bad token, a
  literal that cannot be held exactly, or text the grammar does not allow. An
  integer above the signed 64-bit maximum and a decimal with more digits than
  .NET `decimal` holds are syntax errors.
- **`AXC0036`.** The expression is longer than 2,000 characters. It is not
  parsed.
- **`AXC0037`.** The expression is deeper than 32 levels.
- **`AXC0038`.** The expression has more than 500 syntax nodes.
- **`AXC0046`.** A name that is not in scope, named in the message. This
  includes an unknown field after a `.`, and any `.` path outside a data
  source filter.
- **`AXC0047`.** Operand types an operator, function or rule does not
  accept, at the operator or the call. This includes an `in` item that does
  not fit, two different enums, an enum parameter with a value the field
  lacks, a function or rule argument of the wrong
  type, `coalesce`
  arguments or `if` branches that do not fit each other, and a `date` or
  `dateTime` call without a text literal argument. It also includes a child
  collection used anywhere but as the first argument of an aggregate, such as
  `lineItems == null`, reported at the name, an aggregate whose first
  argument is not a child collection, such as `sum(title, amount)`, and an
  item of a type the aggregate does not accept. It also covers a `.` after a
  field that is not a reference, after a data source parameter, or after
  anything that is not a field path, at the `.`. The message names the
  types, and a collection as `list<Entity>`.
- **`AXC0048`.** The expression's type does not fit the type its use needs.
  The message names the expected and the actual type.
- **`AXC0049`.** A text literal compared with an enum, or given where an enum
  is needed, is not one of the field's `values`. Reported at the literal.
- **`AXC0050`.** A call to a function or rule that does not exist, at the
  call. The message names it.
- **`AXC0051`.** A function or rule call with the wrong number of
  arguments, at the call. This includes an aggregate without its item
  expression, such as `sum(lineItems)`. The message names the expected count, such as "needs 2 arguments" or
  "needs at least 1 argument", and the count found.
- **`AXC0053`.** A data source filter uses something outside the
  [SQL subset](#sql-subset), at the operator, call or path step. This
  includes `/`, `lower`, `upper`, `trim`, every aggregate, and a `date('…')` or
  `dateTime('…')` text that is not valid. The message names what is not
  translated. It is reported only after the filter type-checks.
- **`AXC0058`.** A path takes more than 3 hops, at the `.` that goes past
  the limit.
- **`AXC0055`.** Rules call each other in a cycle. It is reported on the
  rule file, not at a call, and names every rule in the cycle. See
  [the Check step](configuration.md#configuration-pipeline).
- **`AXC0065`.** Rule calls nest more than 8 deep. It is reported once, at
  the rule file of the lowest rule past the limit, not at its callers. The
  message names the rules in the chain in call order. A rule in a cycle, or
  that calls into one, gets only `AXC0055`.
- **First problem only.** The parser and the type checker each stop at the
  first problem and report only that one. So one expression gives at most
  one diagnostic.
