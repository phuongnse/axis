# D50. Sign-in audit records and security scanning in CI

Status: **Agreed**

## Context

Signing in is the first step of every attack on a shared server, and it left no trace. The
dependencies and Axis's own code also need checks that run without anyone remembering them.

## Decision

- **Sign-in is audited.** A sign-in, a sign-out and a refused sign-in write `session.signedIn`,
  `session.signedOut` and `session.signInRefused` to the tenant's audit records. A refusal records
  `anonymous` and a reason code from a closed list. Each record holds the client address, shortened
  ([D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)), and never a
  token, a cookie or the user agent.
- **Routine events are not.** An expired session, a sign-out without a session and a `403` write no
  audit record. `403`s stay in the logs and metrics.
- **A dependency scan.** `scripts/scan.sh` fails on a high or critical vulnerability in the NuGet or
  npm packages that is not accepted within its time frame. CI runs it
  ([D107](d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md)), and it is not a
  NexKit check.
- **Static analysis.** CodeQL scans the C# and the TypeScript into GitHub code scanning. A repository
  rule that the owner sets fails the check on a new high or critical alert, because CodeQL does not.

The detail is in [sign-in and the session](../reference/security.md#sign-in-and-the-session),
[dependency updates](../development.md#dependency-updates) and the
[scripts](../development.md#scripts).

## Consequences

- The client address is personal data, kept as long as the audit records: forever.
- Lower severities pass the scans and wait for the routine update.

## Alternatives

- **Audit every `403` and expiry.** Both are common and would flood the table.
- **A free-text reason.** It could carry personal data into a table kept forever.
- **Fail on every severity.** It would block merges on every low finding.
