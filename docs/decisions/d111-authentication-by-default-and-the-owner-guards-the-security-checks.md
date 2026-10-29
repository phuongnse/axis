# D111. Authentication by default, and the owner guards the security checks

Status: **Agreed**

The owner agreed these points on 2026-10-29. Each route of the server calls the policy module
itself, and nothing in the framework refuses a route that forgets to. NexKit's AI review is the
only gate before a merge, and the same kind of model writes and reviews the code. These points
add checks that need no judgment, and put a person on the one kind of change that can weaken
them.

- **Authentication by default**:
  - The server sets an authorization fallback policy that requires a signed-in user or a valid
    caller token. A route that must be open says so, such as `/health`, `GET /api/site`,
    `GET /api/texts/{locale}` and the sign-in routes.
  - A test reads every route from the server's endpoint data source, as `RateLimitRouteTests`
    does, and calls each one with no session and no token. Each route that is not on the open
    list must answer `401` or `403` without reaching a tenant database. A route added later is
    tested without anyone adding it.
- **Fuzz tests** _(planned for M18)_ for the parsers that read outside input: the expression
  language and its SQL translation, the record API's request bodies, and CSV and spreadsheet
  import. They run with the unit or integration tests, with a fixed seed and a time limit.
- **A penetration test by an independent tester** before the first customer's installation.
  The owner chooses the tester. The report is recorded on the
  [ASVS](../reference/asvs.md#other-evidence) page, and each finding becomes a threat model row
  of [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md).
- **The owner guards the security checks:**
  - A hand-made change adds `.github/CODEOWNERS`, which names the owner for the files that define
    the security checks: the route inventory tests and their shared helper, the fallback
    policy's open list, `scripts/scan-accepted.json`, `scripts/scan.mjs`,
    `scripts/check-docs.mjs`, `scripts/check-raw-html.mjs` and
    `scripts/check-prototype-pollution.mjs`, and the Trivy accepted list once it exists.
  - The owner adds a rule on `main` that requires a code owner's review. NexKit cannot merge a
    pull request that touches those files: it waits, and the owner approves and merges it.
  - Every other pull request still merges by itself, as
    [delivery](../delivery.md#decisions) describes.
  - The owner reviews one question: did the change add an exception to a check, or loosen one,
    and why.

_Why:_ a check that walks every route finds a forgotten authorization the day the route is
added, whoever wrote it. Fuzzing finds the parser bugs that AI tools now find quickly, before an
attacker does. AI writes and reviews most of Axis's code, so the review shares the writer's
blind spots, and an independent tester sees what neither does. A person cannot read every
security change, and one who tries soon approves without reading. The easiest way for an agent
to make a red check green is to add an exception to it, so a person guards the few files that
hold the exceptions, about one pull request a day, each a small diff. The cost is that those
pull requests wait for the owner.
