# D52. Reference pickers, label texts and the expression editor

Status: **Agreed**

## Context

An author who types names, text keys and expressions by hand sees a mistake only after a save, as a
diagnostic of the folder. Studio needs to offer the names that exist, show a label's words and
check an expression as it is typed, without a copy of the compiler's rules in the browser.

## Decision

- **One names route.** The authoring API answers every name the application defines and every text
  key with its texts, from the in-memory compile of the folder. Studio loads it with the application
  and again after a change.
- **The schemas say where a picker and an expression go.** The annotations `x-axis-reference` and
  `x-axis-expression` mark a reference property and an expression property in the resource schemas,
  so the schemas stay the single source of what the form shows, and no resource file changes.
- **A reference is picked from the names.** A name that does not resolve stays visible, marked
  unknown, so nothing is lost.
- **A label is edited as its texts, never as its key.** One route writes a key in every text
  resource at once, so a key is never left in one locale only.
- **An expression is checked where it stands.** A route compiles the folder in memory with the
  expression placed in the saved resource, so the scope, the types and the limits are the compile's
  own. It writes nothing.
- **The expression editor is CodeMirror 6,** in a chunk that loads only when Studio first shows an
  expression. It checks as the author pauses, completes the names in scope and never blocks a save.

The routes are in the [authoring API](../reference/authoring.md#names), the keywords in
[schema keywords](../reference/resource-files.md#schema-keywords), and the pickers, labels and
editor in [Studio](../reference/studio.md) and the
[Studio resource form](../reference/studio-resource-form.md#labels).

## Consequences

- The names answer is large, a few hundred kilobytes at the typical application size.
- A text write takes no version, because one version per file does not fit a write across files.
  Two authors who edit the same key lose one edit, and a crash between two files can leave a key in
  some locales only, which the next compile reports.
- Each pause in typing compiles the folder in memory.
- Completion follows a simple scope rule, so it can offer a name that is not in scope at that exact
  place. The check then flags it.

## Alternatives

- **Monaco as the editor.** It is several times larger than CodeMirror 6 and needs web workers.
- **A route per picker.** Each picker would call the server, and the names would load many times.
