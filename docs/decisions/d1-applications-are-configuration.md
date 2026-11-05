# D1. Applications are configuration

Status: **Agreed**

## Context

Axis runs many applications for many businesses on shared engines. Upgrades, security fixes and the
engines' guarantees can reach every application only when no application has code of its own in the
platform.

## Decision

- An application's business-specific models, queries, rules, processes, UI, policies and text are
  resources in its configuration.
- Platform code contains nothing specific to any application.

## Consequences

- Every application gets each upgrade, security fix and engine guarantee at once.
- A need that one application has, and configuration cannot express, becomes a general feature of
  the platform rather than code for that application.
