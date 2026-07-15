# Axis

Axis is an open-source platform being built for adaptable, workflow-driven business applications.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quickstart

Install Python 3, the .NET SDK from `global.json`, Node from `frontend/.nvmrc`, and Docker Engine with Compose. On WSL/Linux use `python3`; on Windows use `py -3` if `python` is unavailable.

```bash
python3 scripts/axis.py doctor --profile build --strict
python3 scripts/axis.py setup --browsers
python3 scripts/axis.py local-dev up
```

Open the web app at <https://localhost:3000>. For ports, HTTPS setup, troubleshooting, and observability, see [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md).
