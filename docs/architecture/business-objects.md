# Business Objects Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/business-objects/README.md](../use-cases/business-objects/README.md) · [docs/ARCHITECTURE.md](../ARCHITECTURE.md#rules-boundary) · [AGENTS.md](../../AGENTS.md)

This file owns durable Business Objects model and module realization. Use cases own author and submitter goals, observable record contracts, and acceptance evidence.

## Model boundaries

- Business Objects is the modular-monolith bounded context for `BusinessObjectDefinition`, immutable `BusinessObjectDefinitionVersion`, `BusinessObjectFieldDefinition`, and `BusinessObjectRecord`.
- Identity owns Workspace lifecycle. Business Objects stores `workspaceId` only as an external scope identifier and returns non-disclosing outcomes for cross-Workspace access.
- An unpublished definition is mutable and revision-protected. Persisted definition and field keys are stable; labels and ordering remain editable before publication.
- Published versions are immutable record contracts with independent snapshot identity and explicit source-field identity. Publication creates version 1 in the current slice; later-version authoring requires its own product contract.
- Field snapshots preserve type, type-specific configuration, order, label, and exact applied Rule-binding revisions needed for deterministic record submission.

## Rules integration

- Business Objects supplies a transient typed consumer-context schema and requires `record.value` coverage. Rules validates exact definition version, input mapping, type, cardinality, and binding revision through its public Contracts boundary.
- Business Objects persists only exact binding references and snapshots required by its published contract. Rules stays pure and consumer-neutral; Business Objects owns authorization, record state, submission transaction, diagnostics, and stored evaluation evidence.
- Existing published snapshots may evaluate their exact historical Rule version when the Rules contract permits it. New attachments follow current Rules eligibility and never silently retarget a newer version.

## Record realization

- The first generic lifecycle is Draft to Submitted. A valid Rule non-match or evaluation failure leaves the Draft recoverable; no implicit Rejected state is introduced.
- Successful submission atomically persists canonical typed values, Submitted state, actor/timestamps, and exact safe Rule evidence in the Business Objects transaction.
- Submitted records are read-only in this slice and remain tied to the immutable definition and binding revisions used at submission.

## Explicit exclusions

- Business Objects does not generate runtime database tables from definitions.
- Event sourcing, projection rebuild, generic workflow orchestration, and hidden cross-module distributed transactions are not part of the current architecture.
- Public operations remain product-neutral. Product clients own their routes, presentation vocabulary, setup, and end-to-end journeys.
