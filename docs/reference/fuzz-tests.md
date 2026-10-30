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
| Record API request bodies | _(planned for M18)_ | |
| CSV reader, `RecordImportCsv` | Built | `RecordImportFuzzTests`, `Csv_reader_returns_rows_or_refusals_for_any_bytes` |
| Spreadsheet reader, `RecordImportXlsx` | Built | `RecordImportFuzzTests`, `Xlsx_reader_returns_rows_or_refusals_for_any_workbook` |
| Import dry run, `RecordImportCheckHandler` | Built | `RecordImportEndpointTests`, `The_dry_run_of_any_fuzzed_file_completes_checked_or_failed` |

The expression tests leave out the interpreter, which evaluates a checked expression and takes no
outside text. The import tests leave out the commit, which reads only rows that the dry run accepted.

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

## How the import tests fuzz

- **Suites.** `RecordImportFuzzTests` is in `Axis.Data.Tests`, so it runs in `scripts/test.sh`. Run it
  with `dotnet test --project tests/Axis.Data.Tests -c Release --filter-class '*RecordImportFuzz*'`.
  The dry run test is in `Axis.Integration.Tests`, because the check needs PostgreSQL and the work
  item queue. Run it with `scripts/integration.sh --filter-class '*RecordImportEndpointTests*'`. The
  inputs come from `tests/Axis.Data.Tests/ImportFuzzInputs.cs`, which both projects compile.
- **Seed inputs.** A CSV input starts as the header of the test's entity and one to six rows of
  values such as numbers, booleans, dates, empty cells and text that needs quotes. An xlsx input
  starts as a generated workbook with the same header and rows.
- **CSV mutations.** An input is a seed with one to four random edits on its bytes: an insert, a
  deletion, a replacement, a duplicated span, a span of another seed, or a wrap. A token is a
  quote, a line break, a byte order mark, bytes that are not UTF-8, a NUL byte, formula text such as
  `=SUM(A1)`, a cell of 5,000 characters or 200 quotes. In one input of eight the file is random
  bytes. In one of eight it is UTF-16 text with a byte order mark.
- **Workbook mutations.** Each input is one of these:
  - odd cells: shared and inline strings, shared string numbers that do not exist, numbers such as
    `1E400` and `NaN`, dates with odd serials, booleans, error values, formulas with and without a
    saved value, bad cell references, row numbers that are huge or go backwards, and repeated cells;
  - odd encodings of a part: UTF-16 with and without a byte order mark and a declaration, a
    declaration that does not match the bytes, `&#0;`, a DTD with nested entities and an external
    entity;
  - zip damage: a part left out or repeated, one to 3,000 extra entries, a changed CRC, size, method
    or local header offset in the central directory, and random byte flips, inserts and cuts.
- **Bombs.** In one input of eight the workbook is a real zip bomb, built once per run with 101 MiB
  of letters: one cell of that size, an extra part of that size, or the first with the directory
  edited to announce 1 KB. The edits to the directory also reach the limits of 1,000 entries and
  100 MiB at almost no cost.
- **Over-limit rule.** A workbook whose zip directory announces more than 1,000 entries or 100 MiB
  must end with the header error `Unpacks to more than the import limit.` and no header. The test
  measures the bytes the check's thread allocated with `GC.GetAllocatedBytesForCurrentThread`, and
  they must stay under 16 MiB. A workbook whose directory announces a small size for a part that
  unpacks to more must stay under 64 MiB. A control test shows that a workbook under the limits
  with one cell of 20 MiB allocates more than 32 MiB, so the measure sees a read.
- **What fails a run.** An exception, or an input past the time limit. A row with both or neither
  of cells and an error. A row number that does not rise. A CSV row, or a workbook row, whose cells
  differ in number from the header. The test also maps each readable header to a test entity and builds the body and the typed value
  of each cell. A body that is not a JSON object fails the test.
- **Dry run test.** It uploads up to 150 fuzzed CSV and xlsx files for `Item` through the
  endpoint, runs the real check and requires each import to end `checked` or `failed`, with every
  error key `/columns`, `/columns/{n}`, `/rows`, `/rows/{n}` or below a row, or the empty pointer.
  It runs far fewer inputs than the reader tests, so it shows that the dry run copes with bad files
  and does not search deeply.

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
  the parser, and keep the test next to the similar cases. An import failure gives the kind of the
  file and `Convert.FromBase64String("…")`: paste it into `RecordImportCsvTests` or
  `RecordImportXlsxTests`. A file of 4 KB or more shows only its kind and length: replay it by seed.
- **Or rerun.** Run the suite with the printed seed, `AXIS_FUZZ_SEED=2025`. The input has the
  same number again.

## Adding a fuzz test

A later fuzz test, for the record API, uses the same helper,
`tests/Axis.Expressions.Tests/Fuzz.cs`. The file depends on nothing else, so the test project links
it:

```xml
<Compile Include="..\Axis.Expressions.Tests\Fuzz.cs" Link="Fuzz.cs" />
```

`Fuzz.Run(next, check)` draws each input with `next(random)` and passes it to `check`, which
fails by throwing. A test that is not about text passes a `show` function that writes its input.
The helper's own tests are in `FuzzTests`.
