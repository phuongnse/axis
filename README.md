# Axis

Axis is an open-source low-code application platform for building adaptable, data-driven workflow applications.

It provides the core building blocks: custom data models, workflows, forms, and role-based pages, so applications can be assembled and extended on a consistent foundation.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Product vision: [docs/PRODUCT_VISION.md](./docs/PRODUCT_VISION.md)
- Architecture: [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)
- Tech stack and ADRs: [docs/TECH_STACK.md](./docs/TECH_STACK.md)
- Use cases and implementation progress: [docs/use-cases/README.md](./docs/use-cases/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quick start (local dev)

Prerequisite: Docker + Compose v2 and the local HTTPS files from
[docs/playbooks/local-https.md](./docs/playbooks/local-https.md).

From repo root:

```bash
python scripts/axis.py local-dev up
```

Then open:

- Web app: <https://localhost:3000>
- API: <https://localhost:5281>
- API health: <https://localhost:5281/health>

For full local-dev operations, ports, troubleshooting, and observability profile, see:
[docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md)
