# D110. Security alerts and a hardening checklist for the operator

Status: **Agreed**

The owner agreed these points on 2026-10-29. Axis already records its
[security events](../reference/log-inventory.md#security-events), but nothing tells anyone when
they spike. Many ASVS rows are delegated to the operator, such as disk encryption, malware
scanning of uploads and access to logs, and no single page lists them. Everything here is
_(planned for M28)_, see the [roadmap](../roadmap.md#milestones).

- **Alert rules ship with Axis:**
  - The Helm chart ships Prometheus alert rules, off by default and on with one value. The same
    rules are in a file for other installs.
  - They cover a spike of refused sign-ins, refused bearer tokens, `403` answers and `429`
    refusals per tenant, a failing health check, and a startup check that failed.
  - Each security event that the rules read is a metric, not only a log entry or an audit
    record, so the rules need no log search.
- **A hardening checklist:** a section of [installation](../reference/installation.md) lists
  every control that ASVS or the threat model delegates to the operator, with how to meet it and
  how to check it.
- **The server checks itself at start:**
  - Outside Development, the server and the worker check the checklist items they can see, such
    as TLS to the database and an `https` telemetry export.
  - A missing control writes a warning that names it, once at each start, and sets a metric that
    an alert rule reads. It does not stop the start, because the operator may meet the control
    in a way Axis cannot see, such as disk encryption.

_Why:_ an event that is logged but never read finds an attack only after the damage. Shipping
the rules means every install starts with detection on the first day. A checklist turns
"delegated to the operator" into work an operator can do and check. A warning at start catches
an install that skipped a step without blocking one that meets it in its own way. The cost is a
set of rules to keep in step with the events, and warnings an operator may choose to ignore.
