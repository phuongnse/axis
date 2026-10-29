# D50. Sign-in audit records and security scanning in CI

Status: **Agreed**

M10 adds two security baselines. Sign-in, sign-out and refused sign-ins leave
audit records, and that part is built. CI runs static analysis, which is
built. Running the dependency scan in CI is _(planned for M18)_. See
[audit records](../reference/storage.md#audit-records-and-sequence-counters) and
[sign-in and the session](../reference/security.md#sign-in-and-the-session) for the
detail. The ASVS level and its verification stay in M16
([D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)).
The owner confirmed these points on issue #707 on 2026-10-19.

- **Sign-in audit:**
  - Three new audit actions are `session.signedIn`, `session.signedOut` and
    `session.signInRefused`. They are written to `axis.audit_records` in the
    tenant database, the same append-only table as the other actions.
  - The names are in the past tense, like `record.created`, except
    `session.signInRefused`.
  - `session.signedIn` and `session.signedOut` record the user id and the
    display name.
  - `session.signInRefused` records the actor `anonymous` and a reason code. It
    records no user. The reason is `otherTenant`, `invalidToken`,
    `invalidState` or `providerError`. `providerError` covers a person who
    cancels at the identity provider, and a provider error.
  - Every record holds the client address, after the forwarded headers rules of
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
    None holds a token, a cookie or the user agent.
  - A session that expires writes nothing. A sign-out with no session writes
    nothing.
- **Dependency scanning:**
  - A new script, `scripts/scan.sh`, needs network. It runs
    `dotnet list package --vulnerable --include-transitive` on `Axis.slnx`,
    and `npm audit --audit-level=high` in `web` and `tests/e2e`.
  - It fails on a vulnerability of high or critical severity, and prints each
    one with its package and advisory.
  - A finding that `scripts/scan-accepted.json` records with its date passes
    until its time frame ends, see
    [dependency updates](../development.md#dependency-updates).
  - CI runs it on its existing triggers and once a week. A finding on the
    weekly run opens no issue. The red run is the signal.
  - It is not a NexKit check until a hand-made change to `.nexkit/` adds it.
- **Static analysis:**
  - One CodeQL workflow scans `csharp` and `javascript-typescript` on a push
    to `main` and on pull requests. Its alerts go to GitHub code scanning.
    - [D104](d104-codeql-and-the-performance-tests-run-every-night.md) adds a
      scan of `main` every night, because a merge by NexKit starts no push
      workflow.
  - A new alert of high or critical severity fails the check. The owner sets
    this threshold in a repository code scanning rule, because CodeQL does not
    fail a run on alerts by itself.
- **Not in M10:** an audit of `403` refusals, an audit of session expiry, and
  the ASVS level. The first two stay in logs and metrics. The ASVS level stays
  in M16.
  - [D84](d84-asvs-level-2-and-a-server-side-session.md) sets the level:
    ASVS 5.0, Level 2.

_Why:_ signing in is the first step of every attack on a shared server, and
today it leaves no trace. A refused sign-in is rare and worth a record, and an
expired session is routine. A closed list of reason codes keeps free text, and
possibly personal data, out of the table. The client address helps trace an
attack, but it is personal data, and audit records are kept forever, so the
cost is that it stays as long as they do. A `403` refusal is common, so
auditing it would flood the table. A scan that stops at high and critical
blocks the real risks without blocking a merge on every low finding. The cost
is that lower severities pass.
