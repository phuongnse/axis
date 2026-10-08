# Expression language

Detailed reference for the expression language: grammar, types, operators,
null rules, functions, cost limits and the SQL subset. Everything in this file
is *(planned for M2)*, and nothing here is built yet. Dn refers to
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
| `decimal` | `decimal` | Exact. Never rounded unless a function says so. |
| `boolean` | `boolean` | |
| `date` | `date` | A calendar date without a time zone. |
| `date-time` | `date-time` | A UTC instant, to the microsecond. |
| `enum` | `enum` | One value of one field's `values`. |
| `reference` | `reference` | A record of the target entity. |

Two more types exist only inside expressions:

- **`null`.** The type of the literal `null`. It fits any type.
- **`list<Entity>`.** The type of a child collection field. Only
  [aggregates](#functions) accept it.

Rules:

- **Widening.** An integer widens to a decimal when it is mixed with one. No
  other conversion is implicit. Text never becomes a number or a date.
- **Enums.** Two enum values compare only when both come from the same
  field's value set, or when one side is a text literal. A text literal
  compared with an enum must be one of the field's `values`, compared
  ordinally. Otherwise it is a type error at compile time.
- **Result type.** Each use sets the type its expression must have.
  Validation, rule and filter expressions need `boolean`. A computed field
  needs its field's type.

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
  digits after the point. Dividing by zero is a
  [run-time error](#run-time-errors).
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

An empty field is `null`. These rules decide what `null` does.

- **Propagation.** Arithmetic, ordering comparisons and most functions return
  `null` when an operand is `null`. The function table says where a function
  differs.
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

- **Null counts as false at the end.** Wherever the final boolean is used,
  `null` counts as false. This includes validation. So `quantity > 0` fails
  when `quantity` is empty. To allow an empty field, write the idiom:

  ```text
  quantity is null or quantity > 0
  ```

  This differs from a PostgreSQL `CHECK` constraint, which passes when its
  condition is `null`.

- **`if`.** A `null` condition picks the else branch.
- **`coalesce`.** It returns its first argument that is not `null`.
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

- **Date and date-time.** The argument of `date` and `dateTime` must be a text
  literal. It is checked at compile time.
- **Enum values.** An enum value is a text literal, checked against the
  field's `values`. See [Types](#types).
- **Negative numbers.** A negative number is unary minus applied to a
  literal.
- **Current time.** v1 has no `now()` and no `today()`. They come later,
  together with a time zone rule.

## Names and references

- **Letter case.** Names match ignoring letter case. This includes field,
  rule and function names.
- **Bare name.** A bare name is a field of the current record. In a data
  source filter it can also be a data source parameter. When a parameter and a
  field share a name, the data source check reports it.
- **Path.** A path such as `department.name` follows a reference field to a
  field of the target record. A path may take at most 3 hops. A `null`
  reference along the path makes the result `null`.
- **Child collection.** A child collection field gives a `list<Entity>`,
  which only aggregates accept.
- **Rule call.** A named rule is called like a function, such as
  `isLargeRequest(total)`. Arguments are checked against the rule's typed
  parameters. A rule body sees only its declared parameters. Parameter and
  result types are the scalar field types (see
  [Resource file shape](configuration.md#resource-file-shape)). A rule name
  may not reuse a built-in function name.
- **Scope in a computed field.** The expression sees the record's own fields
  and its child collections, and no reference path.
- **Scope in a data source filter.** A filter sees the entity's fields and the
  data source parameters as plain names.

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
| `round(decimal, integer)` | decimal | Rounds half away from zero to the given number of digits after the point. | Yes |
| `floor(n)` | same as `n` | | Yes |
| `ceiling(n)` | same as `n` | | Yes |
| `year(date)` | integer | | Yes |
| `month(date)` | integer | 1 to 12. | Yes |
| `day(date)` | integer | 1 to 31. | Yes |
| `addDays(date, integer)` | date | | Yes |
| `daysBetween(date, date)` | integer | Second minus first. | Yes |
| `coalesce(a, b, …)` | type of the arguments | First argument that is not `null`. All arguments have the same type. | Yes |
| `if(condition, then, else)` | type of the branches | A `null` condition picks `else`. Both branches have the same type. | Yes |
| `date(text literal)` | date | See [Literals](#literals). | Yes |
| `dateTime(text literal)` | date-time | See [Literals](#literals). | Yes |

The date functions work on `date` only, not on `date-time`. Taking a calendar
day from an instant needs a time zone, and the time zone rule comes later.

### Aggregates

An aggregate works on a child collection. The second argument is an item
expression. Inside it, names refer only to the child row.

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
- **Null items.** `sum`, `min` and `max` skip `null` items.
- **Condition.** A `null` condition counts as false.
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

Run-time limit:

- **Step budget.** Each top-level evaluation has a budget of 10,000 steps.
  Each node evaluated is one step. An aggregate's item expression costs its
  steps once per row. The steps of a called rule count against the caller's
  budget.
- **SQL.** The SQL translation has no step budget. The compile-time limits
  bound its size.

## Run-time errors

These are errors:

- division by zero;
- integer overflow;
- a date outside 0001 to 9999;
- an exhausted step budget.

An error stops the evaluation and counts as a failure. A validation fails, a
policy denies, and a computed field rejects the write. A run-time error is not
a compile diagnostic.

## SQL subset

These translate to SQL:

- literals, sent as parameters;
- field paths, where each hop becomes a join;
- `==` and `!=`, as `IS NOT DISTINCT FROM` and `IS DISTINCT FROM`;
- `<`, `<=`, `>` and `>=`;
- `+`, `-`, `*` and unary `-`;
- `and`, `or` and `not`, because PostgreSQL is also three-valued;
- `is null` and `is not null`;
- `in`, as `(x IS NOT NULL AND x IN ($1, $2, …))`, with every item sent as a
  parameter;
- `if`, as `CASE WHEN … THEN … ELSE … END`;
- `coalesce` and `concat`;
- `length`, as `char_length`;
- `contains`, as `strpos(a, b) > 0`;
- `startsWith`, as `starts_with`, and `endsWith`;
- `abs`, `round`, `floor` and `ceiling`;
- `year`, `month` and `day`, through `extract`;
- `addDays`, as `date + integer`;
- `daysBetween`, as `b - a`;
- rule calls, inlined when the rule body is in the subset.

These are left out:

- **`/`.** The scale of a division result is set by the interpreter alone.
- **`lower` and `upper`.** Unicode case rules differ between .NET and
  PostgreSQL.
- **`trim`.** The two systems define whitespace differently.
- **Aggregates.** A collection needs a scope that a filter does not have.

Rules:

- **Not in the subset.** A filter that uses something left out is a compile
  diagnostic in the type family.
- **Null rows.** `WHERE` drops rows where the condition is `null`. This
  matches `null` counting as false.
- **Parameters.** Values are always sent as parameters. They are never
  spliced into the SQL text.

## Writing expressions in resource files

An expression is one JSON string:

```json
{
  "validation": "quantity is null or quantity > 0"
}
```

The property name above is illustrative. The issue for each feature defines
the real property names. For `expression` on a field, on a validation and on a
rule, see [Entity logic](configuration.md#entity-logic).

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
  cycles.

Each diagnostic points at the JSON Pointer of the expression string. Its
message gives the character position inside the expression. The `AXCnnnn`
codes are added by the issues that build each check.
