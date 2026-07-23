# Rules

> **Navigation**: [docs/use-cases/README.md](../README.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

Rules owns reusable system and workspace rule definitions, immutable versions, parameter and context schemas, a versioned typed-expression language with registered capabilities, and pure deterministic evaluation. The system defines the safe language; for workspace-authored rules, each user controls how available fields, parameters, operators, functions, and logical groups are composed. System definitions remain code-owned and read-only. Consumer modules own applied snapshots, authorization, enforcement transactions, side effects, and runtime business state.

## Current Use Cases

| Use case | Status |
|---|---|
| [docs/use-cases/rules/evaluate-published-rules.md](./evaluate-published-rules.md) | Done |
| [docs/use-cases/rules/manage-workspace-rule-definitions.md](./manage-workspace-rule-definitions.md) | Done |
| [docs/use-cases/rules/provide-system-field-rule-definitions.md](./provide-system-field-rule-definitions.md) | Done |
