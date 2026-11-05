# D111. Authentication by default, and the owner guards the security checks

Status: **Agreed**

## Context

Each route calls the policy module itself, and nothing refuses a route that forgets to. The same kind
of AI model writes and reviews the code, so the review shares the writer's blind spots, and the
easiest way to turn a red check green is to add an exception to it.

## Decision

- **Authentication by default.** A fallback policy requires a session or a valid caller token, and an
  open route says so. A test calls every route without either, so a route added later is covered.
- **Fuzz tests** for the parsers of outside input: expressions and their SQL, record API bodies, and
  import. They use a fixed seed and a time limit.
- **An independent penetration test** before the first customer's installation. Each finding becomes
  a threat model row ([D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md)).
- **The owner guards the security checks.** `.github/CODEOWNERS` and a rule on `main` make a pull
  request that touches the checks or their exceptions wait for the owner, who asks whether it adds or
  loosens an exception, and why.

The detail is in [authorization](../reference/security.md#authorization),
[fuzz tests](../reference/fuzz-tests.md) and [delivery](../delivery.md#decisions).

## Consequences

- A forgotten authorization is found the day its route is added.
- About one small pull request a day waits for the owner. The rest merge by themselves.

## Alternatives

- **The owner reviews every security change.** A person who tries soon approves without reading.
