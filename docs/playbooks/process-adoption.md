# Automatic process adoption

> **Navigation**: [docs/README.md](../README.md) ·
> [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

Axis adopts public engineering-process releases through an ordinary, non-automerge
Renovate pull request.

## Owners

- requirements/process.in owns the exact public package pin.
- requirements/process.txt owns the complete pip-compile hash lock.
- .process/process.lock and .agents/skills are managed adoption outputs.
- .github/renovate.json5 owns explicit consumer intent and the exact post-upgrade
  command.
- Axis CI, independent review, and the configured human owner decide whether the PR
  can merge.

After Renovate updates the pin and compiled lock, the administrator allowlist permits
only:

    python .process/adopt-process.py --project-root . --requirements-lock requirements/process.txt

Shell execution remains disabled. The existing runner installs the target package
from the new hash lock in an isolated environment. That target atomically updates the
managed process lock, skills, templates, AGENTS block, and project-schema migration.
Renovate includes those paths in the same commit. Repeating the operation must produce
no diff.

The PR then runs normal development, review, security, and policy-verification checks.
A package-only update, stale managed file, failed profile, or self-review remains
blocking. The configured human owner alone merges the independently reviewed exact
checkpoint; merge activates the new process. No post-merge command or automerge is
permitted.
