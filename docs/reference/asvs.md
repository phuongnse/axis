# ASVS

This page tracks how Axis verifies itself against OWASP ASVS 5.0 at Level 2: one row for each
requirement, on the pages it links, with its status and the evidence for it, and the other evidence
that ASVS does not cover. Level 2 and the server-side session it needs are chosen in
[D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md).

## How it is filled

Each requirement has one row, and its status says who meets it and what shows that. A script checks
the tables, and a reviewer judges what the script cannot.

- **One row per requirement.** The tables on the pages that [requirements](#requirements) links list
  every Level 1 and Level 2 requirement of ASVS 5.0 once, in the standard's order, by its id, such as
  `V6.2.1`.
- **A status for each.** The status is one of:
  - **Met:** Axis meets it. The evidence names a test class, a doc section or a check that shows it.
  - **Delegated:** the identity provider or the deployment's operator meets it. The identity
    provider meets multi-factor authentication, password rules, account lockout, account
    recovery and SAML assertion processing, because Axis has no passwords of its own, see
    [sign-in and the session](security.md#sign-in-and-the-session). The operator meets malware
    scanning of uploads, see [file fields](file-fields.md#field-and-value).
  - **Not applicable:** Axis has nothing the requirement covers. The evidence says why.
- **Checked.** `scripts/check-docs.mjs` reads the committed list of ids,
  [`asvs-5.0.0.json`](asvs-5.0.0.json), which holds the id and level of every Level 1 and Level 2
  requirement. It comes from OWASP's flat JSON export of ASVS 5.0.0, named in the file's `source`.
  The requirement text on those pages is OWASP's, under CC BY-SA 4.0.
  - It finds each table by its header line, wherever it lives under `docs/`.
  - It fails on an id that is not in the list or that has two rows, on one page or on two, on a status
    that is not in the list above, and on a row without evidence.
  - It fails on a **Met** row that names a test class, in backticks and ending in `Tests`, that no
    `.cs` file under `tests/`, `controlplane/tests/` or `samples/` declares. It does not judge that
    the test covers the requirement. A reviewer does.
  - It fails on a listed id that has no row on any page, so every listed id appears exactly once.
  - It fails on every **Gap** row, with or without an issue link. Checking that an issue is open would
    make the offline check call GitHub. A gap found later is closed in the change that finds it, or
    that change waits for the issue that closes it.
- **Kept current.** A change that adds a route, a setting or a stored secret checks the rows it
  touches, in the same change.
- **Sign-off** _(planned for M32)_. A final pass over every row confirms that each requirement is
  met, delegated or not applicable.

## Other evidence

Some evidence of Axis's security is not a single ASVS requirement. This section lists it and where
each piece is kept.

- **FAPI 2.0 conformance.** The result of the OpenID Foundation's conformance suite against the
  development realm under `fapi2`, with its date, see
  [FAPI 2.0 profile](fapi2-profile.md).
- **Dependency scan and static analysis.** `scripts/scan.sh`, the image scan
  `scripts/scan-images.sh` and the CodeQL workflow, see [development](../development.md#scripts).
- **Threat model.** What can go wrong in Axis's own design, and what Axis does about each threat,
  see [the threat model](threat-model.md).
- **SAMM rating** _(planned for M32)_. Once a year the owner records here a rating of Axis against
  the Threat Assessment practice of OWASP SAMM.
- **Fuzz tests.** Random and mutated input for the parsers that read outside input: which are
  fuzzed, with what seed and limits, and how to replay a failure, see [fuzz tests](fuzz-tests.md).
- **Penetration test.** An independent tester's report, with its date, before the first
  customer's installation.

## Requirements

The requirement rows live on three pages, by ASVS chapter, with OWASP's text, their status and their evidence.

- [Input and the web](asvs-input-and-web.md): chapters V1 to V5, on input, the web front end, APIs and files.
- [Identity and access](asvs-identity-and-access.md): chapters V6 to V10, on authentication, sessions, authorization, tokens and OAuth.
- [Data and operations](asvs-data-and-operations.md): chapters V11 to V17, on cryptography, communication, configuration, data, secure coding, logging and WebRTC.
