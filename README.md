# Axis

Axis is an open-source platform being built for adaptable, workflow-driven business applications.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quickstart

Install the Python launcher from [docs/playbooks/scripts.md § Tool Versions](./docs/playbooks/scripts.md#tool-versions), plus Git, Docker Engine with Compose, and OpenSSL; `python --version` must resolve Python 3 in the active environment. `axis setup` can install the pinned .NET SDK and Node.js into a user-local Axis directory; it never installs OS packages, changes services, or requires Docker Desktop.

Run on any supported host:

```bash
python scripts/axis.py setup --profile local-dev --install-user-tools
python scripts/axis.py local-dev up
```

Add `--plan-only` to inspect the platform-specific work without changing anything. When host-browser access is required, add `--trust-local-ca` to opt into current-user host trust, then run `python scripts/axis.py local-dev host-smoke` after the stack is ready. For supported-platform trust boundaries, Docker-in-WSL, troubleshooting, and observability, see [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md).
