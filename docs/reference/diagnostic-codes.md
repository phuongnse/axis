# Diagnostic codes

The [configuration pipeline](configuration.md) reports every problem it finds in an application
folder as a diagnostic. This page describes what a diagnostic holds and links the pages that list every
code with its meaning, one page for each topic, so that an author can look up what a code means and
where it is reported.

## Diagnostic shape

A diagnostic carries `file`, `resourceId` when the file has a readable id, `path`, `code` and a
message. Compile reports all diagnostics, not just the first, sorted by file and then path.

- **`file`** is relative to the application folder and uses `/` separators, or is empty when the
  diagnostic concerns the whole application folder.
- **`path`** is a JSON Pointer (RFC 6901) into that file, such as `/fields/0/type`, or is empty when
  the problem concerns the whole file or the whole folder.
- **The message** never contains absolute paths or exception text, because it is shown to
  application authors.

## Codes

Codes have the form `AXCnnnn` and never change meaning. Each code has one row, in the table of the page
for its topic. The pages list their codes in number order.

| Page | Holds the codes of |
| --- | --- |
| [Resource diagnostic codes](diagnostic-codes-resources.md) | Resources, and every code that no other page holds |
| [Expression diagnostic codes](diagnostic-codes-expressions.md) | The expression language |
| [Process diagnostic codes](diagnostic-codes-processes.md) | Processes, and the triggers, events, endpoints, connectors, webhooks and notifications that they use |
| [Package and module diagnostic codes](diagnostic-codes-packages.md) | Modules, packages, contracts and customization layers |

A code's topic decides its page. A rule about the expression language goes to the expression page. A
rule about a module, package, contract or customization layer goes to the package page. A rule about a
process, trigger, event, endpoint, connector, webhook or notification goes to the process page. Every
other rule goes to the resource page.

A new code takes the next number after the highest code on any page. Its row goes at the end of the
table on the page of its topic. A retired code is never reported again and keeps its row.

A unit test reads every table under `docs/` and compares them with `DiagnosticCodes` and
`ExpressionDiagnosticCodes`. It fails for a code that is on no page or on two.
