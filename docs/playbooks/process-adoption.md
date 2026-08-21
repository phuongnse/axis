# Automatic process adoption

> **Navigation**: [docs/README.md](../README.md) ·
> [docs/TECH_STACK.md](../TECH_STACK.md) · [AGENTS.md](../../AGENTS.md)

Axis pulls public engineering-process releases through Renovate. The publisher never
writes into this repository and no consumer depends on a sibling checkout.

## Version and automation owners

- `requirements/process.in` owns the direct public package pin.
- `requirements/process.txt` is the pip-compile output containing the complete graph
  and accepted hashes.
- `.process/process.lock` owns the adopted distribution digest and selected skills.
- `.github/renovate.json5` owns polling, pip-compile, the bounded output allowlist, and
  `automerge: false`.
- `.process/adopt-process.py` is the managed bootstrap runner. It installs only the
  hash-locked target distribution in an isolated environment; that installed target
  owns `processctl adoption apply`.

The Renovate host administrator must allow only this literal command:

~~~text
python .process/adopt-process.py --project-root . --requirements-lock requirements/process.txt
~~~

Repository configuration cannot grant itself this host permission. If the command is
not allowlisted, the update is incomplete and must fail instead of advancing only the
package pin.

## Complete adoption candidate

Before opening its draft, Renovate updates the input pin, regenerates the compiled
lock, and runs the managed runner. The same branch must contain the process lock,
managed skills and templates, and any predeclared
`.process/adoption-migrations/<target-version>.json`. The target distribution applies
only declared consumer policy; it never infers an Axis configuration migration.

CI installs the candidate lock and runs target-owned `processctl adoption check` on
the complete branch before the normal development and review profiles. A package-only
or partially materialized proposal fails closed. Renovate may discover an already
published release on its next polling run; there is no publisher dispatch credential.

After CI and independent review, merging the exact PR is the adoption action. No
post-merge command, synchronization, or automerge is permitted by default.
