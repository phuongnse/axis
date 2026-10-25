# ASVS

How Axis tracks its verification against OWASP ASVS 5.0, Level 2. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md).

The design follows [D84](../decisions/d84-asvs-level-2-and-a-server-side-session.md). This page is
_(planned for M16)_: its structure is agreed, and the requirements are filled in by the M16 issues.

## How it is filled

- **One row per requirement.** The [requirements](#requirements) table lists every Level 1 and
  Level 2 requirement of ASVS 5.0 once, in the standard's order, by its id, such as `V6.2.1`.
- **A status for each.** The status is one of:
  - **Met:** Axis meets it. The evidence names a test class, a doc section or a check that shows it.
  - **Gap:** Axis does not meet it yet. The evidence names the issue that closes it.
  - **Delegated:** the identity provider meets it, such as multi-factor authentication, password
    rules, account lockout and account recovery. Axis has no passwords of its own
    ([D9](../decisions/d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md)).
  - **Not applicable:** Axis has nothing the requirement covers. The evidence says why.
- **Checked.** `scripts/check-docs.mjs` checks that every Level 1 and Level 2 id appears exactly once,
  and that every row has a status from the list above.
- **Kept current.** A change that adds a route, a setting or a stored secret checks the rows it
  touches, in the same change.
- **Sign-off.** The final pass comes last in M16, after M14 and M15 have added their routes. Every
  row is then met, delegated or not applicable.

## Other evidence

- **FAPI 2.0 conformance.** The result of the OpenID Foundation's conformance suite against the
  development realm under `fapi2`, with its date, see
  [FAPI 2.0 profile](security.md#fapi-20-profile).
- **Dependency scan and static analysis.** `scripts/scan.sh` and the CodeQL workflow, see
  [development](../development.md#scripts).

## Requirements

| Id  | Requirement | Status | Evidence |
| --- | ----------- | ------ | -------- |
