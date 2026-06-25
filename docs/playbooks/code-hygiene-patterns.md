# Code Hygiene Patterns

> **Navigation**: [<- docs/README.md](../README.md) . [<- AGENTS.md](../../AGENTS.md)

Small hygiene rules keep reviews focused on behavior.

## Code hygiene checklist

### 1. No inline fully-qualified type names

Add `using` directives instead of writing fully-qualified names inline unless ambiguity requires it.

### 2. No restructuring to avoid a `using` directive

Do not change code shape just to dodge an import.

### 3. Verify `!` is actually needed before adding it

Search existing call sites and fix the nullability cause when possible.

### 4. No scaffold placeholder files

Delete `Class1.cs`, placeholder files, stubs, and generated template leftovers.

### 5. User input flowing into external identifiers

Validate and normalize user-provided names before using them in schema/table/topic/file/external identifiers.

### 6. No direct commits to `main`

Use feature branches. Review fixes stay on the PR branch.

## Drift script regex constraints

### Keep rules readable

Policy regexes should encode reusable invariants, not one incident. Prefer typed parsing when practical.

### Validation before commit

Run the focused check for the rule you changed, then `$axis-ready-review` before review.
