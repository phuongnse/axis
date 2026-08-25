# Automatic process adoption

> **Navigation**: [docs/README.md](../README.md) ·
> [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

Axis receives public engineering-process release intent through its configured
consumer lifecycle host. Renovate continues to own normal dependency proposals, but
its engineering-process authority rule is disabled because that PR-first path cannot
satisfy checkpoint review before source publication. The publisher never writes into
this repository and no consumer depends on a sibling checkout.

## Version and automation owners

- `requirements/process.in` owns the direct public package pin.
- `requirements/process.txt` is the pip-compile output containing the complete graph
  and accepted hashes.
- `.process/process.lock` owns the adopted distribution digest and selected skills.
- `.github/renovate.json5` owns explicit consumer discovery intent, normal dependency
  polling, the disabled process-authority rule, the absence of post-upgrade execution,
  and `automerge: false`.
- `.process/adopt-process.py` is the managed bootstrap runner. It installs only the
  hash-locked target distribution in an isolated environment; that installed target
  owns `processctl adoption apply`.

The consumer-selected lifecycle host runs only this literal adoption command after
materializing the unpublished candidate:

~~~text
python .process/adopt-process.py --project-root . --requirements-lock requirements/process.txt
~~~

Repository configuration cannot grant itself host execution authority. If the host
cannot run this exact command, the update is incomplete and must fail instead of
advancing only the package pin.

## Complete adoption candidate

Before publishing source, the consumer lifecycle host updates the input pin,
regenerates the compiled lock, and runs the managed runner in an unpublished local
checkpoint. That checkpoint must contain the process lock, managed skills and
templates, and any predeclared
`.process/adoption-migrations/<target-version>.json`. The target distribution applies
only declared consumer policy; it never infers an Axis configuration migration.

The host installs the candidate lock and runs target-owned `processctl adoption check`
before the normal development and review profiles. A package-only or partially
materialized proposal fails closed. A fresh independent agent or human then reviews
the exact verified checkpoint; findings repeat implementation and every invalidated
profile. Only after `change finish` and `publication validate-source` may automation
push the branch and create the PR.

The configured human owner alone merges the exact completed PR; that merge is the
adoption action. No post-merge command, synchronization, or automerge is permitted.
