# Rules

> **Navigation**: [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

Rules owns reusable `RuleDefinition` and `RuleBinding` aggregates, their persistence, safe authoring projections, validation, immutable versions, activation, usage discovery, and pure evaluation. A Rule is a versioned, deterministic, side-effect-free contract with `Inputs -> Logic -> Outputs`; the current language may expose narrower capabilities without changing those platform terms. Consumer modules map their runtime data into declared inputs and own target authorization, lifecycle, output interpretation, and every reaction or side effect.

Rule definition, application, and execution stay distinct. The definition declares inputs, canonical logic, and outputs. A binding records where the rule is used and maps consumer sources into inputs. An execution resolves those mappings, evaluates one exact version, and returns typed outputs plus safe diagnostics; it never gives logic ambient access to the platform context.

## Current Use Cases

| Use case | Status |
|---|---|
| [docs/use-cases/rules/manage-workspace-rule-definitions.md](./manage-workspace-rule-definitions.md) | Partial |
| [docs/use-cases/rules/manage-rule-bindings.md](./manage-rule-bindings.md) | Partial |
| [docs/use-cases/rules/evaluate-published-rules.md](./evaluate-published-rules.md) | Partial |
| [docs/use-cases/rules/provide-built-in-rule-definitions.md](./provide-built-in-rule-definitions.md) | Partial |

Built-in and workspace origins use one semantic definition, version, binding, and evaluation contract. Business Objects is the first consumer, not an owner or dependency of Rules.
