# Axis agent instructions

## Before you start

- Read [docs/product-brief.md](docs/product-brief.md) and [docs/decisions.md](docs/decisions.md)
  before any product or architecture work.
- [docs/architecture.md](docs/architecture.md) is the target structure.
- [docs/domain/concepts.md](docs/domain/concepts.md) defines the vocabulary. Use those
  terms in code, APIs and UI.
- [docs/domain/knowledge.md](docs/domain/knowledge.md) explains why the design rules exist.
  Check it before changing configuration, execution, storage or security behaviour.

## Language

Use English for source code, comments, configuration, commit messages,
issues and repository documentation. The owner may discuss things in
Vietnamese.

## Working rules

- Docs are the source of truth. An issue links to the relevant section; it
  does not copy it. If a change alters agreed behaviour, update the doc in the
  same change.
- Keep the [decisions](docs/decisions.md) status words exact: **Agreed**
  means agreed, and **Proposed** means still open. Never present a proposal as
  agreed.
- Each issue is one behaviour. It has 2–5 acceptance criteria that a test or
  command can verify, and it fits in a single agent session. Split anything
  larger before starting.
- Build in vertical slices. Every milestone ends with something that runs
  end to end and is covered by an automated test.
- Every behaviour change needs a test. Use real PostgreSQL through
  Testcontainers for storage and engine behaviour. Use Playwright for UI
  journeys. Test denied access as well as allowed access.
- No application-specific code in the platform. Purchase requests and other
  sample applications live as configuration under `samples/`.
- Never commit secrets, tokens, local logs or machine snapshots.

## Commands

The commands are defined in milestone M0 (see the [roadmap](docs/roadmap.md)).
Until then there is nothing to build.
