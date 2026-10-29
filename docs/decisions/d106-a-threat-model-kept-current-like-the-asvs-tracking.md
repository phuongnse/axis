# D106. A threat model kept current like the ASVS tracking

Status: **Agreed**

The owner agreed these points on 2026-10-29. [ASVS 5.0 Level 2](../reference/asvs.md)
checks Axis against controls that every web application needs, and
[D84](d84-asvs-level-2-and-a-server-side-session.md) set it. It does not ask what can go wrong
in Axis itself: a tenant that reads another tenant's records, an author who can change every
policy, an extension package that holds every tenant's secrets. Axis gets a threat model for
that. It is built _(planned for M18)_, before the final ASVS sign-off.

- **Scope:** three parts.
  - The product: the server, the worker, the SPA, tenants, Studio and authoring, extension
    packages, connectors, endpoints and webhooks, and the identity provider Axis ships
    ([D99](d99-axis-ships-its-own-identity-provider.md)).
  - The installation: the images, Docker Compose, the Helm chart and an install on premises.
  - The way Axis is built: NexKit, CI, and the AI agents that write to the repository.
- **Method:**
  - The four questions of the Threat Modeling Manifesto frame it: what are we working on, what
    can go wrong, what are we going to do about it, and did we do a good job.
  - STRIDE finds the threats at each trust boundary, such as the browser and the server, the
    server and a tenant database, an author and a release, the worker and a package or a
    connector, and NexKit and the repository.
  - LINDDUN finds the privacy threats to personal data.
  - OWASP's agentic AI threats find the threats to the AI agents that write and review code.
- **The page:** `docs/reference/threat-model.md`.
  - A data flow diagram in Mermaid for each trust boundary.
  - One row per threat, with an id, the boundary, the STRIDE or LINDDUN category, a rating and
    a status.
- **Status and evidence,** with the rules of the ASVS tracking:
  - **Mitigated** names its evidence: a test class, a doc section or a check.
  - **Gap** links the GitHub issue that closes it.
  - **Delegated** names who meets it: the identity provider or the operator.
  - **Accepted** gives the reason and the date the owner accepted it. Only the owner accepts a
    threat.
- **Rating:** Low, Medium, High or Critical, from likelihood and impact, a short form of the
  OWASP Risk Rating Methodology.
  - A Gap has the time frames of [dependency updates](../development.md#dependency-updates):
    Critical 7 days, High 30 days, Medium 90 days, Low at the next milestone. The time starts
    on the day the row is written.
- **Checked:** `scripts/check-docs.mjs` checks the table as it checks the ASVS table: a known
  status, a rating, evidence on every row, a test class that exists for **Mitigated**, an issue
  link for **Gap**, and a date for **Accepted**.
- **Kept current:**
  - A change that adds a trust boundary updates the page in the same change: a new kind of
    route, host, caller, stored secret or integration.
  - Each milestone's docs sweep reviews the page, with the ASVS rows its milestone touched.
  - Once a year the owner rates Axis against the Threat Assessment practice of OWASP SAMM,
    recorded on the [ASVS](../reference/asvs.md#other-evidence) page.

_Why:_ ASVS asks whether Axis has the controls every application needs. Only a threat model asks
what an attacker can do with Axis's own design: tenants, policies, authoring, packages, and the
agents that write most of the code. No standard certifies a threat model, so Axis takes the
widely used methods and holds the result to the same rules as the ASVS tracking: one place, one
row per threat, evidence that a check can read. The cost is one more page that each new trust
boundary must update, and one more review in each docs sweep.
