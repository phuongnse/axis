# D6. In-configuration logic uses a typed expression language

Status: **Agreed**

## Context

Validation, computed fields, conditions, routing and policy filters need logic. When authors can
embed general-purpose code in configuration, it grows into large, duplicated and untested scripts
whose side effects the engines cannot see, as
[logic in configuration](../domain/knowledge.md#logic-in-configuration) describes.

## Decision

- Logic in configuration is written in a small, typed expression language without side effects,
  which Axis interprets.
- Expressions are type-checked when the release is compiled.
- Evaluating an expression has a bounded cost.
- Named rules let an application reuse an expression.
- No script, SQL or general-purpose code can be embedded in configuration.

The language is [D16](d16-expression-language.md)'s, and its reference is
[expressions](../reference/expressions.md).

## Consequences

- The engines know everything an expression can do, so they can keep their guarantees of retries,
  idempotency and authorization.
- Logic with side effects or real algorithmic content cannot live in configuration. It becomes an
  operation, built in or written in an extension package.
