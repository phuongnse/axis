# Axis

Axis is an open-source platform being built for adaptable, workflow-driven business applications.

## Quick links

- Documentation hub: [docs/README.md](./docs/README.md)
- Contribution guide: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Agent/workflow rules: [AGENTS.md](./AGENTS.md)

## Quickstart

Install Python 3.14 with `venv`/`pip` from [docs/playbooks/scripts.md § Tool Versions](./docs/playbooks/scripts.md#tool-versions), plus Git, Docker Engine with Compose, and OpenSSL. The published engineering-process runtime installs verified managed developer tools when a supported artifact exists; it never installs OS packages, changes services, or requires Docker Desktop.

Run on any supported host:

```bash
python -m venv .process-venv
# Activate .process-venv using the native command for your shell.
python -m pip install --require-hashes -r requirements/process.txt
processctl setup --project-root . --profile development --apply --allow network --allow user-files --allow project-files
python scripts/axis.py local-dev certs
python scripts/axis.py install-hooks
python scripts/axis.py local-dev up
```

Run `processctl setup --project-root . --profile development` without `--apply` to inspect the platform-neutral setup plan. When host-browser access is required, opt into current-user trust with `python scripts/axis.py local-dev trust-certs`, then run `python scripts/axis.py local-dev host-smoke` after the stack is ready. For supported-platform trust boundaries, Docker-in-WSL, troubleshooting, and observability, see [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md).
