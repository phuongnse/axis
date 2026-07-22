# Axis

Axis is an open-source platform being built for adaptable, workflow-driven business applications.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quickstart

Install Python 3, Git, Docker Engine with Compose, and OpenSSL in the environment where Axis will run. `axis setup` can install the pinned .NET SDK and Node.js into a user-local Axis directory; it never installs OS packages, changes services, or requires Docker Desktop.

WSL/Linux:

```bash
python3 scripts/axis.py setup --profile local-dev --install-user-tools
python3 scripts/axis.py local-dev up
```

Windows PowerShell:

```powershell
py -3 scripts/axis.py setup --profile local-dev --install-user-tools
py -3 scripts/axis.py local-dev up
```

Add `--plan-only` to inspect the platform-specific work without changing anything, or `--trust-local-ca` to opt into the current-user host trust store (`--yes` skips the Axis prompt; Windows may still warn). Use the host URLs printed after the stack becomes ready. For supported platforms, Docker-in-WSL, HTTPS setup, troubleshooting, and observability, see [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md).
