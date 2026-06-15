# Axis

Axis is an open-source low-code application platform for building adaptable, data-driven workflow applications.

It provides the core building blocks: custom data models, workflows, forms, and role-based pages, so applications can be assembled, extended, and governed from one place without starting from scratch.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Product vision: [docs/PRODUCT_VISION.md](./docs/PRODUCT_VISION.md)
- Architecture: [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)
- Tech stack and ADRs: [docs/TECH_STACK.md](./docs/TECH_STACK.md)
- Use cases and implementation progress: [docs/use-cases/README.md](./docs/use-cases/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quick start (local dev)

Prerequisite: Docker + Compose v2.

From repo root:

```bash
docker compose up -d
```

Then open:

- Web app: <http://localhost:3000>
- API: <http://localhost:5280>
- API health: <http://localhost:5280/health>

For full local-dev operations, ports, troubleshooting, and observability profile, see:
[docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md)
