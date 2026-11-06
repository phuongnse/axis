# D2. Axis owns its engines

Status: **Agreed**

## Context

Versioning, durability, authorization and inspection have to work the same way across every
engine that runs an application. An engine that Axis does not own would set its own semantics for
all four.

## Decision

- Axis implements its own configuration compiler, expression engine, data engine, process engine,
  presentation runtime and policy engine.
- Third-party libraries are used for infrastructure: HTTP, database access, serialization, UI
  components, standard security protocols and telemetry.
- Axis uses no third-party workflow or rule engine.

## Consequences

- One model of versioning, durability, authorization and inspection holds in every engine.
- Axis builds and maintains each engine itself.

## Alternatives

- **A third-party workflow or rule engine.** It would bring its own versioning, durability,
  authorization and inspection, which would differ from the rest of Axis.
