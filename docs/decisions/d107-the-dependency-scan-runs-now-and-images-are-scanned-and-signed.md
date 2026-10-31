# D107. The dependency scan runs now, and images are scanned and signed

Status: **Agreed**

The owner agreed these points on 2026-10-29. [D50](d50-sign-in-audit-records-and-security-scanning-in-ci.md)
built `scripts/scan.sh` and left running it in CI to M18. Run by hand on 2026-10-29, it failed
on five high findings in development packages of `web/`, which nothing had reported. The time
from a published vulnerability to its exploit keeps getting shorter, so Axis watches its
dependencies at once and extends the watch to its images.

- **The dependency scan runs in CI now.** A hand-made change to `.github/workflows/` runs
  `scripts/scan.sh` on CI's triggers and once a week, as D50 describes. It replaces D50's
  plan to run it in M18. It is still not a NexKit check: a red scan on `main` opens NexKit's `bug`
  issue for CI on `main`, like any failing check.
- **Today's findings are fixed at once,** by one issue in no milestone, before the time frames
  of [dependency updates](../development.md#dependency-updates) end.
- **Dependabot alerts are on, and Dependabot opens no pull requests.**
  - The alerts cover NuGet, npm, GitHub Actions and the Dockerfile's base images.
  - The owner turns the alerts on at once, and GitHub notifies the owner of each one.
  - _(planned for M28)_ The hourly driver opens one NexKit issue in no milestone for each high or
    critical alert that no open issue covers, so every change still goes through NexKit with its
    checks and review. The [`axis-nexkit-routine` skill](../../.claude/skills/axis-nexkit-routine/SKILL.md)
    gets that step.
  - The time frames of dependency updates start on the alert's date.
- **Images are scanned, described and signed:** the scan is _(planned for M28)_, and the release
  workflow with its SBOMs and signatures is _(planned for M32)_, as
  [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) moves it.
  - CI builds the three Axis images and scans each one, and the identity provider's image that
    Axis pins, with Trivy. A high or critical finding fails the scan, unless it is accepted in
    the same way as `scripts/scan-accepted.json`, with the same time frames.
  - A release workflow builds the images from a version tag, scans them, writes a CycloneDX
    SBOM for each, signs each image and its SBOM with cosign keyless signing through the
    workflow's GitHub identity, and publishes them to the GitHub container registry.
  - [Installation](../reference/installation.md#images) tells the operator how to check the
    signature before an install or an upgrade.
  - The release workflow is in `.github/`, so the hourly driver makes it as a hand-made change
    from a NexKit plan.

_Why:_ a scan that only runs by hand is a scan that does not run. The time frames start when
the scan first finds a vulnerability, so a scan that never runs never starts them: the five
findings were development packages, so customers were not exposed, but nobody knew of them. Dependabot learns of a new advisory the day it is published, and
the hourly driver turns it into work that NexKit tests and reviews, instead of a pull request
outside NexKit. An image holds an operating system and runtime packages that the NuGet and npm
scan never reads. A signed image with its SBOM lets a customer check what they run and that Axis
built it. The cost is a scan on every CI run, a release workflow to keep working, and alerts the
driver must turn into issues.
