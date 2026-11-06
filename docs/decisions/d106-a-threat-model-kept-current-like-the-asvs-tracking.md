# D106. A threat model kept current like the ASVS tracking

Status: **Agreed**

## Context

The ASVS tracking of [D84](d84-asvs-level-2-and-a-server-side-session.md) checks the controls every
web application needs. It does not ask what can go wrong in Axis's own design: tenants, policies,
authoring, packages and the AI agents that write most of the code. No standard certifies a threat
model.

## Decision

- **Three scopes:** the product, the installation, and the way Axis is built, NexKit, CI and the
  agents included.
- **Widely used methods.** The Threat Modeling Manifesto's four questions frame it. STRIDE finds the
  threats at each trust boundary, LINDDUN the privacy threats, and OWASP's agentic AI threats those
  to the agents.
- **One row per threat,** rated from likelihood and impact, with a status: **Mitigated**, **Gap**,
  **Delegated** or **Accepted**, each with its evidence. Only the owner accepts a threat. A gap has
  the time frame of a vulnerable dependency.
- **Checked** by `scripts/check-docs.mjs`, as the ASVS table is.
- **Kept current.** A change that adds a trust boundary updates the model. Each docs sweep reviews
  it, and once a year the owner rates Axis against OWASP SAMM's Threat Assessment.

The detail is in [threat model method](../reference/threat-model-method.md).

## Consequences

- An attacker's use of Axis's design is written down, with what Axis does about it.
- Each new trust boundary updates one more page, and each docs sweep has one more review.
