# D107. The dependency scan runs now, and images are scanned and signed

Status: **Agreed**

## Context

A scan that runs only by hand does not run, and the time from a published vulnerability to its
exploit keeps getting shorter. An image holds an operating system and runtime packages that the
package scan of [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md) never reads, and a
customer cannot check that an image is the one Axis built.

## Decision

- **CI runs `scripts/scan.sh`** on its triggers and once a week. A red scan on `main` opens NexKit's
  `bug` issue for CI on `main`, like any failing check.
- **Dependabot alerts, no Dependabot pull requests.** The hourly driver opens a NexKit issue for each
  high or critical alert that no open issue covers. A time frame starts on the alert's date.
- **Images are scanned.** CI scans the Axis images and the pinned identity provider image with Trivy,
  under the dependency scan's rules.
- **Released images are signed.** A release workflow publishes each image with a CycloneDX SBOM,
  both signed with cosign keyless signing, and the operator checks the signature before installing.

The detail is in [dependency updates](../development.md#dependency-updates) and
[images](../reference/installation.md#images).

## Consequences

- A new advisory becomes NexKit work the day it is published.
- A customer can check what they run and who built it.
- CI pays for the scans, and the release workflow must keep working.

## Alternatives

- **Dependabot pull requests.** They would bypass NexKit's checks and review.
