# Threat model method

This page says how the [threat model](threat-model.md) is filled: how threats are found, rated
and given a status, what the check verifies, and how the model is kept current.

## How it is filled

Each threat is one row in the table of the trust boundary it crosses, found with a named method and
rated by a short form of a standard rating.

- **The four questions.** The Threat Modeling Manifesto frames the model. What are we working on?
  What can go wrong? What are we going to do about it? Did we do a good job?
- **Methods.**
  - STRIDE finds the threats at each trust boundary.
  - LINDDUN finds the privacy threats to personal data.
  - OWASP's agentic AI threats find the threats to the AI agents that write and review code.
- **A diagram for each boundary.** Each trust boundary has a data flow diagram in Mermaid, on the
  page of its scope.
- **One row per threat.** The table has these columns: id, threat, boundary, category, rating,
  status and evidence. A scope letter and a number make an id, such as `P1`, `I1` or `B1`. An id is
  unique across every threat model file, so a later row or issue can name a threat by its id alone.
  The model is split by scope, one file for each, see [threat model](threat-model.md).
- **Rating.** The rating is Low, Medium, High or Critical. It comes from likelihood and impact, a
  short form of the OWASP Risk Rating Methodology.
  - A **Gap** has time to close, as a vulnerable dependency does, see
    [dependency updates](../development.md#dependency-updates). Critical is 7 days, High 30 days,
    Medium 90 days (Moderate in that table) and Low the next routine update.
  - The time starts on the day the row is written.
- **Status.** Each row has one of four statuses.
  - **Mitigated:** Axis has a control. The evidence names a test class in backticks, ending in
    `Tests`, a doc section or a check.
  - **Gap:** Axis does not have the control yet. The evidence links the GitHub issue that closes it.
  - **Delegated:** the identity provider or the operator meets the threat. The evidence names which.
  - **Accepted:** the owner accepts the threat. The evidence gives the reason and the date, as
    YYYY-MM-DD. Only the owner accepts a threat.
- **Open threats.** A threat that no issue closes has no row, because a **Gap** row links its
  issue. It is listed below its boundary's table, with its category and rating, and becomes a
  **Gap** row when the issue that closes it is opened.
- **Checked.** `scripts/check-docs.mjs` fails on these problems:
  - an id that another row already uses, in the same file or in another threat model file
  - a status that is not one of the four above
  - a rating that is missing or is not one of the four above
  - a row without evidence
  - a **Mitigated** row that names a test class no `.cs` file under `tests/` or `samples/` declares
  - a **Gap** row that links no GitHub issue
  - an **Accepted** row that gives no date
  - It does not judge that a test covers its threat, that a time frame was kept, or that a
    **Delegated** row names the right party. A reviewer does.
- **Kept current.** A change that adds a trust boundary updates the threat model in the same
  change, see [the threat model rule](../writing-docs.md#how-the-docs-stay-correct). Each docs sweep
  reviews the model.
- **SAMM rating** _(planned for M32)_. Once a year the owner rates Axis against OWASP SAMM, see
  [ASVS other evidence](asvs.md#other-evidence).
