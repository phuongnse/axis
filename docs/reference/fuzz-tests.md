# Fuzz tests

Detailed reference for the fuzz tests of the parsers that read outside input. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md).

The design follows [D111](../decisions/d111-authentication-by-default-and-the-owner-guards-the-security-checks.md).
A fuzz test feeds a parser random and mutated input. It runs with the unit or integration tests,
with a fixed seed and a time limit, so a run is repeatable and fast. It does not use a
coverage-guided fuzzer, so it needs no instrumentation and no extra package.

## The fuzzed parsers

| Parser | Status | Suite |
| ------ | ------ | ----- |
| Expression parser, `ExpressionParser` | Built | `ExpressionFuzzTests`, `Parser_returns_a_tree_or_a_diagnostic_for_any_text` |
| Expression type checker, `ExpressionTypeChecker` | Built | `ExpressionFuzzTests`, `Checker_returns_a_type_or_a_diagnostic_for_any_parsed_expression` |
| SQL translator, `SqlTranslator` | Built | `ExpressionFuzzTests`, `Translator_keeps_every_input_value_in_a_parameter` |
| Record API request bodies | Built | `RecordBodyFuzzTests` in `Axis.Server.Tests`, `Create_body_is_accepted_or_refused_with_a_400_problem` and `Update_body_is_parsed_or_refused_with_a_400_problem`, and `RecordBodyFuzzTests` in `Axis.Integration.Tests`, `Create_answers_an_allowed_status_and_an_accepted_body_leaves_a_valid_record` and `Update_answers_an_allowed_status_and_an_accepted_body_leaves_a_valid_record` |
| CSV and spreadsheet import | _(planned for M18)_ | |

The expression tests leave out the interpreter, which evaluates a checked expression and takes no
outside text.

The unit suite of the record API tests fuzzes the body parsing, `RecordInputParser`, and the
checks of a create that need no database, in `RecordCommand`. The integration suite sends the same
kind of bodies over HTTP. Neither fuzzes the query parameters of a list or the file upload.

## How the expression tests fuzz

- **Suite.** The `Axis.Expressions.Tests` project, in `scripts/test.sh`. Run one with
  `dotnet test --project tests/Axis.Expressions.Tests -c Release --filter-class '*ExpressionFuzz*'`.
- **Seed corpus.** Every expression in the four samples under `samples/apps`: the value of each
  `expression`, `filter` and `when` property, and of each property whose name ends in `When`, as
  [writing expressions in resource files](expressions.md#writing-expressions-in-resource-files)
  lists. A test fails when the corpus holds fewer than 25 expressions, so a renamed property
  cannot empty it.
- **Mutations.** An input is a corpus entry with one to four random edits, or, in one input of
  four, a run of one to 40 tokens. An edit inserts a token, deletes a span, replaces a span with a
  token, duplicates a span, splices in part of another entry, or wraps the text as `(x)`,
  `not (x)` or `x and x`. A token is an operator, a keyword, a field, rule or function name, an
  odd character, a long number, 40 open parentheses or SQL injection text such as
  `'x''); DROP TABLE t; --'`.
- **Scope.** The type checker and the translator see the fields, the enums, a reference field, a
  child collection and the two rules that the samples use, `FinanceThreshold` and
  `departments:IsManager`.
- **Translator inputs.** Each parsed tree is translated twice: as parsed, which the translator's
  API allows, and with the type checker's result when the tree passes it. Most mutated inputs fail
  the checker, so the first translation keeps the translator busy.

## How the record API tests fuzz

- **Suites.** `RecordBodyFuzzTests` in the `Axis.Server.Tests` project runs in `scripts/test.sh`.
  Run it with `dotnet test --project tests/Axis.Server.Tests -c Release --filter-class '*RecordBodyFuzz*'`.
  `RecordBodyFuzzTests` in the `Axis.Integration.Tests` project needs Docker and runs in
  `scripts/integration.sh`. Run it with `scripts/integration.sh --filter-class '*RecordBodyFuzz*'`.
- **The entity.** `Specimen` in the FilesApp fixture, `tests/Axis.Integration.Tests/Fixtures/FilesApp`,
  has a field of every type: text, integer, decimal, money, boolean, date, date-time, enum, reference,
  references, file and a child collection. It also has a required field, a unique field, a computed
  field, three validations, and a field with a minimum and a maximum. The unit suite compiles the
  fixture without a database. The user `requester` holds every grant on `Specimen`, so a `403` is a
  wrong refusal.
- **The valid base.** `RecordBodyFuzz`, in `tests/Axis.Server.Tests`, builds a valid create or update
  body. Each field has a random valid value or is left out. The body has up to three rows of the
  child collection and up to two ids in the references field. An update body names a version. Both
  suites link the file, so both draw the same bodies.
- **The edits.** In one input of eight the body stays as it is. Otherwise it gets one to four edits
  of its JSON tree. An edit replaces a value with an edge token, deletes a property, adds an unknown
  property or one with another letter case, sets a computed field, nests a value 70 arrays deep, gives
  `tags` 1,001 ids, a repeated id or too many ids, gives the child collection too many rows or a row
  that is not an object, or changes `version`. An edge token is, for example, `1e1000000`, `-0`,
  `9223372036854775808`, `"2026-02-30"`, `"2026-10-06T24:00:00Z"`, a lone surrogate, a text with a
  null character, a text of 5,000 characters, an enum value in the wrong letter case, a malformed id,
  `null`, `{}`, `[]` or `true`. In one input of four the serialized bytes get one to three more edits:
  a span is deleted or duplicated, which gives duplicate keys, a byte such as `0x00`, `0xFF` or `0xC3 0x28`
  or a character such as `{`, `"` or `\` is inserted, or the body is cut short. A body is under 64 KB.
  In the integration suite one request in eight is sent as `text/plain`, with no content type, or as
  `application/json; charset=utf-8`.
- **What the unit suite checks.** A create body goes through `RecordCommand.ParseCreate` and then
  `PrepareCreate`, which applies the defaults and runs the computed fields and the validations. An
  update body goes through `ParseUpdate`. A refusal is mapped through `RecordEndpoints.WriteFailure`
  and must be a `400` with problem details that hold errors by JSON Pointer. A test shows that
  unmutated bodies pass both steps, and another that the edited bodies are neither all accepted nor
  all refused, so the fuzzer reaches both paths.
- **What the integration suite checks.** Every answer is a `200`, `201`, `400`, `404`, `409`, `413` or
  `415`, never a `500`. An answer that is not a `2xx` is a problem that leaks no SQL, storage name or
  stack trace, and a `400` holds errors. The update test sends the bodies to one record and names the
  version of the last write, so a share of them is accepted.
- **The read-back checks.** After every `2xx` the record reads back with a `200`. It passes
  `RecordValidator.ValidateUpdate`, which runs the entity's validations on the stored values and rows.
  Its stored values, with the computed, sequenced and creator fields, the unique `code` and the
  file left out, also parse again as a create body. So an accepted write never leaves a value that the
  field rules refuse. The `attachment` field is left out because an uploaded file attaches once. A
  fuzzed body names one of three uploaded files in one input of ten, so a file attaches
  now and then.
- **Rate limit.** The fixture host raises `RateLimits:Session`, because a run sends thousands of
  requests as one user.

## Budgets and overrides

| Setting | Default | Override |
| ------- | ------- | -------- |
| Seed | `2025` | `AXIS_FUZZ_SEED` |
| Time budget of each test | 5 seconds | `AXIS_FUZZ_SECONDS` |
| Inputs of each test | 20,000, or no limit when `AXIS_FUZZ_SECONDS` is set | |
| Time limit of one input | 2 seconds | |

All inputs come from one random generator with the seed, so the same seed gives the same inputs in
the same order. On a slow machine a test stops after fewer inputs, but the first inputs never
change. For a deeper search by hand, run the suite with a longer budget and other seeds, for
example `AXIS_FUZZ_SEED=7 AXIS_FUZZ_SECONDS=120`.

## What fails a run

- **An exception.** The parser, the checker or the translator throws. A rejected input is not an
  exception: each stage answers with a tree, a type or SQL, or with a diagnostic, and exactly one
  of the two.
- **A hang.** One input runs past the time limit of 2 seconds. The thread of a hung input keeps
  running until the test process ends.
- **SQL that holds input text.** Every token of the SQL the translator emits must be one of:
  - a parameter that the result names, or `@currentUser`;
  - a quoted name that the test's column and collection callbacks give;
  - the number `0` or `1`;
  - a word or punctuation mark that the translator writes itself, such as `AND`, `IS DISTINCT FROM`,
    `COALESCE`, `AT TIME ZONE` or `(`.

  Anything else fails the test and names the token, such as a quoted text, a number or a name that
  came from the input. The text of `now()` is removed first. A test shows that the check rejects
  such tokens, so it cannot pass by accepting everything.

## Replaying a failure

A failure message gives the seed, the number of the input and the input as a C# string literal:

```text
Fuzz input 1342 of seed 2025 (rerun with AXIS_FUZZ_SEED=2025): "t == 'x'' and (("
```

- **Copy the literal.** Paste it into a regular unit test in `ExpressionParserTests`,
  `ExpressionTypeCheckerTests` or `SqlTranslatorTests`, with a name that says what it shows. Fix
  the parser, and keep the test next to the similar cases. A record body goes into
  `RecordInputParserTests` in `Axis.Data.Tests`, or, when the failure is in a command step, into
  `Axis.Server.Tests` or the integration tests of the record API.
- **A `base64:` input.** The message of a record body shows its bytes as a C# literal when they are
  UTF-8 text. Otherwise it shows `base64:` and the bytes in base64. Decode them, for example with
  `Convert.FromBase64String`, and use the bytes as the test's body.
- **Or rerun.** Run the suite with the printed seed, `AXIS_FUZZ_SEED=2025`. The input has the
  same number again.

## Adding a fuzz test

A later fuzz test, for import for example, uses the same helper,
`tests/Axis.Expressions.Tests/Fuzz.cs`. The file depends on nothing else, so the test project links
it:

```xml
<Compile Include="..\Axis.Expressions.Tests\Fuzz.cs" Link="Fuzz.cs" />
```

`Fuzz.Run(next, check)` draws each input with `next(random)` and passes it to `check`, which
fails by throwing. A check that awaits, such as one that calls an HTTP endpoint, uses
`Fuzz.RunAsync(next, check)` with a check that returns a task. It has the same seed, budget and
limits, and it checks the inputs one after the other. A test that is not about text passes a `show`
function that writes its input. The helper's own tests are in `FuzzTests`.
