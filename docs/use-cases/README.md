# Use Cases

> **Navigation**: [← docs/README.md](../README.md)

Use cases are the user-facing source of truth for behavior. Each use case should be self-contained: flow, AC, wireframes, diagrams, and implementation status are in one place.

## Structure

```text
docs/use-cases/
├── README.md
├── _template-use-case.md
└── <domain>/
    └── <use-case>.md
```

## Rules

- No numeric IDs required.
- Use stable slugs in file names (e.g. `switch-language.md`, `sign-in.md`).
- Keep engineering quality gates in shared playbooks, not as end-user use cases.
- Every use case must include:
  - Purpose / actor / trigger
  - Main flow + alternate/error flows
  - Acceptance criteria
  - Wireframes table
  - Diagrams table (or explicit N/A)
  - Implementation status callout
