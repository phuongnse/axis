---
name: axis-use-case-implementation
description: Orchestrate an Axis product slice from a ready use-case contract through layer implementation, acceptance evidence, status reconciliation, and verification. Use when changing documented product behavior across backend or frontend layers.
---

# Axis Use Case Implementation

## Goal

Ship one reviewable product slice while preserving the owning contract, layer order, delegated decisions, and acceptance evidence.

## Hard gates

Follow [reference.md](../reference.md).
- A missing or blocked spec **Requires** `$axis-use-case-spec` before code.
- Non-trivial work **Requires** current `$axis-design-gate` evidence; high-risk work stops for sign-off.
- Do not mark a layer or use case **Done** without passing evidence for every in-scope required AT.
- Non-trivial implementation **Requires** an independent Sol review at High reasoning when available before completion; primary verification is not a substitute.

## Inputs

- Ready use-case file and [AC/AT authoring contract](../axis-use-case-spec/reference.md).
- In-scope AC/AT rows and current Design Gate/architecture decisions.
- Existing layer code, tests, status, and evidence sidecar.

## Workflow

1. Locate the owning spec and every applicable foundation contract; confirm each required expected result is supported by its AC, flow, or decision. This workflow **Delegates** product gaps to `$axis-use-case-spec`, foundation gaps to `$axis-frontend-foundation`, and resumes only with **Ready** evidence.
2. Carry or obtain Design Gate evidence. This workflow **Delegates** foundational module decisions to `$axis-module-architecture`, adopted tactical work to `$axis-module-patterns`, wire changes to `$axis-api-contract`, and SPA slices to `$axis-frontend-feature`; each delegate returns evidence here.
3. Map every in-scope product and foundation AC to required AT rows and its lowest reliable automated boundary before implementation. For reusable evaluators, preserve the owning definition/application/execution split through every layer instead of introducing consumer-specific models or ambient context. Keep browser ATs independently runnable and outcome-focused; do not use one end-to-end test as a substitute for component, API, or persistence evidence.
4. Work in dependency order: Domain, Application, Infrastructure, API, Frontend. Do not start a higher layer while a required lower-layer gap is unresolved. For an approved clean cutover, replace every layer and caller in the same slice; do not leave a compatibility branch.
5. Add or update proving tests first when practical; implement narrowly using same-module patterns and preserve business-safe failures, thin endpoints, and generated contracts.
6. Reconcile implementation status and the evidence sidecar with changed paths and required ATs. Record exact deferred ACs. For an approved clean cutover, run the dossier's identifier sweep and remove obsolete compatibility tests and guidance; preserve compatibility evidence when the dossier requires compatibility. Mark an AT `N/A` only when the owning contract proves it out of scope or non-applicable; every other required AT needs passing evidence.
7. Run every required AT command plus focused static checks. Delegate the immutable implementation checkpoint to the independent reviewer, integrate any findings, and rerun affected verification before returning acceptance evidence, status, and gaps to the caller. Publication is separate.

## Output

Report implemented slice, AT evidence, checks, status/docs changes, delegated decisions, and exact gaps/deferrals.
