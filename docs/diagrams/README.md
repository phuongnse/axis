# Architecture diagrams

[← Back to Docs Home](../README.md)

`docs/diagrams/` holds system-level architecture diagrams.

## Agent rule (required)

When changing any file under `docs/diagrams/`, agents must run the checklist in
[`REVIEW_CHECKLIST.md`](./REVIEW_CHECKLIST.md) before committing.

## Source of truth

- Edit diagram source in `*.excalidraw` files.
- Keep diagram content aligned with:
  - [`../ARCHITECTURE.md`](../ARCHITECTURE.md)
  - [`../TECH_STACK.md`](../TECH_STACK.md) ADRs
  - [`../PROGRESS.md`](../PROGRESS.md)
  - [`../../CLAUDE.md`](../../CLAUDE.md)

## Regenerate

1. Regenerate `.excalidraw` sources from generator:

   ```bash
   node docs/diagrams/generate-diagrams.mjs
   ```

2. Regenerate `.svg` previews from `.excalidraw` files:

   - PowerShell:

     ```powershell
     .\docs\scripts\generate-diagrams.ps1
     ```

3. Run review checklist:

   - [`REVIEW_CHECKLIST.md`](./REVIEW_CHECKLIST.md)

## Current architecture assumptions in these diagrams

- Modulith with strict service boundaries.
- Cross-module events/snapshots via Kafka + Schema Registry (Avro + CloudEvents).
- Commands/jobs/saga steps via RabbitMQ.
- Sync RPC via gRPC contracts (escape hatch).
- Per-module PostgreSQL databases (`axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_workflowengine`, `axis_formbuilder`, `axis_pagebuilder`).
