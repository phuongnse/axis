# D2. Axis owns its engines

Status: **Agreed**

Axis implements its own configuration compiler, expression engine, data engine,
process engine, presentation runtime and policy engine. Third-party libraries
are fine for infrastructure: HTTP, database access, serialization, UI
components, standard security protocols and telemetry. No third-party workflow
or rule engine.

_Why:_ versioning, durability, authorization and inspection have to work the
same way across every engine. An external engine would set its own semantics
for all four.
