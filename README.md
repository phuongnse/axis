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
processctl setup --project-root . --profile development --apply
python scripts/axis.py local-dev certs
python scripts/axis.py install-hooks
python scripts/axis.py local-dev up
```

`requirements/process.in` owns the direct public process pin and
`requirements/process.txt` owns its complete pip-compile hash graph. The configured
consumer lifecycle host prepares and reviews a complete unpublished adoption
checkpoint with the managed `.process/adopt-process.py` command, then pushes and
creates the PR only after lifecycle completion. Human merge of that exact PR applies
the new process; there is no command to run after merge. Operational details are in
[docs/playbooks/process-adoption.md](./docs/playbooks/process-adoption.md).

Run `processctl setup --project-root . --profile development` without `--apply` to inspect the consumer-owned dependency command. When host-browser access is required, opt into current-user trust with `python scripts/axis.py local-dev trust-certs`, then run `python scripts/axis.py local-dev host-smoke` after the stack is ready. For supported-platform trust boundaries, Docker-in-WSL, troubleshooting, and observability, see [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md).
