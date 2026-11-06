# D110. Security alerts and a hardening checklist for the operator

Status: **Agreed**

## Context

An event that is logged and never read finds an attack only after the damage. Many ASVS rows are
delegated to the operator, such as disk encryption and malware scanning of uploads, and no page
turned them into work an operator can do and check.

## Decision

- **Alert rules ship with Axis,** off by default in the Helm chart and in a file for other installs,
  for spikes of the [security events](../reference/log-inventory.md#security-events), failing health
  checks and failed startup checks. Each event they read is a metric.
- **A hardening checklist** in the installation guide lists every control delegated to the operator,
  with how to meet and check it.
- **The hosts check themselves at start.** Outside Development, a missing control that the server or
  the worker can see writes a warning and sets a metric, but does not stop the start, because the
  operator may meet it in a way Axis cannot see.

The detail is in the [hardening checklist](../reference/installation.md#hardening-checklist).

## Consequences

- Every install can detect an attack from its first day.
- The rules must follow the events, and an operator may ignore a warning.
