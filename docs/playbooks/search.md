# Search

> **Navigation**: [docs/README.md](../README.md) · [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

Search is a server-owned CQRS read concern. Product modules own searchable fields, authorization, workspace scope, result identity, and provider ports; Infrastructure owns indexes, analyzers, query translation, and read-store technology.

## Contract

- Use query text, locale, scope, and required paging or product criteria; never expose database syntax.
- Constrain authorization and workspace candidates before matching, ranking, limiting, or highlighting.
- Keep provider scores internal. Stable API behavior is ordered results plus structured original-text highlight segments where needed.
- Match case-insensitively and diacritic-insensitively, ignore query-term order, tolerate minor spelling mistakes, and rank exact/title matches above weaker matches.
- Prove exact, prefix, reordered multi-term, diacritic-free, and minor-typo queries with a deterministic relevance corpus.

## CQRS and storage

- Module Application defines side-effect-free search queries and storage-neutral provider ports.
- Module Infrastructure implements the port against its current read store; domain projects and the frontend never reference an engine.
- PostgreSQL providers use migration-backed full-text and trigram indexes with accent normalization; persisted collections cannot use unindexed fuzzy whole-table scans.
- A future MongoDB or dedicated-search read store replaces the provider and projection without changing client semantics.
- A future cross-module global search uses one materialized search read model built from published module contracts; do not merge provider-native scores from independent stores.
- Do not introduce events, outbox/inbox, replay, a global-search route, or another storage adapter without its owning contract and Design Gate.

## Client

[docs/foundations/search/search-experience.md](../foundations/search/search-experience.md) owns debounce, stale-request behavior, paging reset, rendering states, and semantic highlights. The client never re-ranks or performs fuzzy matching.
