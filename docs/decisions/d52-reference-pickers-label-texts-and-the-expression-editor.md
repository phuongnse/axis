# D52. Reference pickers, label texts and the expression editor

Status: **Agreed**

Studio lets an author pick references, edit label texts in every locale and
write expressions with live type checks, in place of typing names, keys and
expression strings by hand. The names route, the reference pickers, label texts with the texts
route, the expression check route and the expression editor are built. See
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
for M11, the [authoring API](../reference/authoring.md#names) for the routes, the
[schema keywords](../reference/resource-files.md#schema-keywords) and
[Studio](../reference/studio.md). The owner confirmed these points on
issue #779 on 2026-10-20.

- **Names:**
  - `GET /api/studio/apps/{application}/names` answers every name the
    application defines, from the in-memory compile of the folder: entities
    with their fields, data sources with their parameters and columns,
    processes with their steps and outcomes, rules with their parameters and
    result type, connectors with their operations, events with their fields,
    roles, sequences, forms, pages and text keys.
  - It also answers the application's locales, and for each text key its text
    in every locale and how many labels use it.
  - Module entities and rules are included and marked with their module. A
    folder with errors still answers with what resolved.
  - It touches no database. The answers for `401`, `403` and `404` are those
    of the rest of the authoring API.
- **Reference pickers:**
  - The form shows a property marked as a reference as a picker over the
    names.
  - A name that does not resolve stays visible, marked as unknown, so nothing
    is lost.
  - The names load with the application, and again after a save, a delete, a
    text write and a reconnect.
- **Schema keywords:**
  - `"x-axis-reference"` marks a reference property. It names a kind, such as
    `entity`, or a part, such as the fields of the entity at another path.
  - `"x-axis-expression": true` marks a string property that holds an
    expression, so the form shows the expression editor there.
  - Both are annotations. The schemas stay the single source of what the form
    shows, and no resource file changes.
- **Label texts:**
  - A label property shows its text in each locale of the application, not
    its key.
  - `PUT /api/studio/apps/{application}/texts/{key}` with
    `{ texts: { <locale>: <text> } }` writes the key in every text resource.
    It needs every locale and answers `400` when one is missing.
  - It writes all text files under the folder write lock. It takes no
    `If-Match`, so the last write to a key wins. Other keys are untouched.
  - A new label gets a key made from the resource and the path, such as
    `pages.myTasks.title`. The author can change it. The text is written
    right away, and the key goes into the resource draft and is saved with
    the next Save.
  - A key that other labels use shows how many, so the author knows the edit
    changes them too.
- **Expression check:**
  - `POST /api/studio/apps/{application}/expressions/check` with
    `{ resourceId, path, expression }` checks one expression in the place it
    would stand: the same names in scope, the same expected type and the same
    limits as the compile.
  - It uses the saved resource to find that place and takes no draft content.
    A path with no expression place is `400`.
  - It answers `{ type, diagnostics }`. A diagnostic has its code, message and
    a range `{ start, end }` in UTF-16 code units of the expression text, with
    an exclusive end. There is at most one, because the compile stops at the
    first problem.
  - It compiles in memory and writes nothing. It is not a save.
- **Expression editor:**
  - An expression property shows a code editor with the expression language's
    highlighting.
  - It checks the text 300 ms after the author stops typing, underlines the
    diagnostic at its range and shows the result type.
  - It completes the names in scope from the names route.
  - A check that gets no answer shows nothing new. It never blocks a save.
- **Editor library:** the editor is CodeMirror 6, at about 120 KB gzipped, in a
  chunk that loads only when Studio first shows an expression.

_Why:_ [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
plans reference pickers and an expression editor with type errors as you type
in M11. Names, keys and expressions were typed by hand before it, and a mistake
shows only after a save, as a folder diagnostic. One names route lets Studio
load everything once, in place of a call per picker. One shared keyword per
purpose keeps the schemas the single source of what is a reference or an
expression. CodeMirror 6 is several times smaller than Monaco and needs no web
workers, and a lazy chunk keeps site pages free of a Studio-only editor. The
costs:

- **A larger answer.** The names route holds every name and every text. At the
  typical application size that is a few hundred kilobytes, once per save.
- **Last write wins on one key.** A text write takes no `If-Match`, so two
  authors who edit the same key lose one edit. Other keys are safe.
- **Not atomic across files.** A crash between two renames can leave a key in
  some locales only. The next compile reports `AXC0027`.
- **A compile per check.** Each pause in typing compiles the folder in memory.
  This may be slow for a large application. The build issue sets the budget.
- **A range the compiler lacks.** The compile reports only a character
  position today. The build issue adds the range.
- **Simple completion.** Completion offers names by a simple scope rule, so it
  can offer a name that is not in scope at that exact place. The check then
  flags it.
- **Unused keys.** A new key is written before the Save, so an abandoned draft
  leaves an unused key. That does no harm, because an unused key is allowed.
