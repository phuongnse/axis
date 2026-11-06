# D137. Authentication by default, and the owner guards the security checks alone

Status: **Agreed**

## Context

Each route calls the policy module itself, and nothing refuses a route that forgets to. The same kind
of AI model writes and reviews the code, so the review shares the writer's blind spots, and the
easiest way to turn a red check green is to add an exception to it.

Autopilot merges NexKit's pull requests by itself, and a pull request that needs the owner's review
waits until the owner reads it. Most changes to a check tighten it, such as a baseline that goes down
or a check that covers one more file. If the owner had to review every one of them, the owner would
hold up autopilot without gaining anything.

## Decision

- **Authentication by default.** A fallback policy requires a session or a valid caller token, and an
  open route says so. A test calls every route without either, so a route added later is covered.
- **Fuzz tests** for the parsers of outside input: expressions and their SQL, record API bodies, and
  import. They use a fixed seed and a time limit.
- **An independent penetration test** before the first customer's installation. Each finding becomes
  a threat model row ([D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md)).
- **The owner guards the security checks.** `.github/CODEOWNERS` and a rule on `main` make a pull
  request that touches a security check, its exceptions or the authentication default wait for the
  owner. The owner asks whether the change accepts a risk, adds an exception or loosens a check, and
  why. The security checks are the dependency and image scans with their accepted lists, and the raw
  HTML and prototype pollution checks.
- **The review guards the other checks.** An item of NexKit's review checklist refuses a change that
  adds an exception to any check or loosens one, such as a baseline count that rises or a rule
  removed from a check script. Then NexKit mentions the owner. A change that tightens a check merges
  by itself.

The detail is in [authorization](../reference/security.md#authorization),
[fuzz tests](../reference/fuzz-tests.md) and [delivery](../delivery.md#decisions).

## Consequences

- A forgotten authorization is found the day its route is added.
- A few pull requests a week wait for the owner: those that touch a security check or the
  authentication default. The rest merge by themselves.
- If a change loosens a check that is not a security check, only the AI review stands in the way,
  with the blind spots that the build threat model accepts.

## Alternatives

- **The owner reviews every security change.** A person who tries soon approves without reading.
- **The owner reviews every change to any check.** Most such changes tighten the check, so the owner
  holds up autopilot without gaining anything.
- **No code owner at all.** The AI review would be the only guard of the security checks, and its
  blind spots are the ones a check is there to cover.
