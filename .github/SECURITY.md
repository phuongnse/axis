# Security policy

## Reporting a vulnerability

Please report a vulnerability in Axis privately, through GitHub's
[private vulnerability reporting](https://github.com/phuongnse/axis/security/advisories/new). Do not open a
public issue, pull request or discussion about it.

A useful report says which version or commit you tested, what an attacker can do, and the steps or a
proof of concept that show it.

## What to expect

- The project owner answers within 7 days, and tells you whether the report is confirmed.
- A confirmed vulnerability is fixed within the time frame of its severity: 7 days for critical, 30 days
  for high and 90 days for moderate.
- It is published in a GitHub security advisory when its fix ships, and at the latest 90 days after your
  report, unless you and the project owner agree on another date. The advisory names the affected
  versions, the fixed version, the severity and what an operator should do, and requests a CVE when one
  applies.

## Supported versions

Only the latest version of Axis gets security fixes. An operator gets a fix by upgrading, in the order
that the [installation guide](../docs/reference/installation.md) gives.

The reasons for this policy are in
[D108](../docs/decisions/d108-reporting-a-vulnerability-and-security-fixes.md).
