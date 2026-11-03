# Legal links and terms

Detailed reference for the privacy policy and terms of service links, the legal line that shows them, the page that
lists the licences of what Axis ships, and the step where a person accepts the terms. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md). The rules come from [D128](../decisions/d128-rules-from-the-studio-review.md). All of
this page is _(planned for M31)_.

## Legal settings

- **The organization owns its policies.** A privacy policy and terms of service are the organization's, because it
  decides what it collects about its people and on what terms. In Axis SaaS the organization is the controller and Axis
  the processor, see [D118](../decisions/d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). Axis writes no
  policy text and ships no default one: it only links what the organization publishes.
- **Set per tenant, with a site override.** A tenant sets the `https` addresses of its privacy policy and its terms,
  and the version of its terms. A site may set its own two addresses, which replace the tenant's on that site's legal
  line only. The commands are in [legal links](tenant-setting-commands.md#legal-links). Under the `configuration`
  tenancy source, the tenant's `Legal` block holds the same values, see
  [tenancy settings](host-settings.md#tenancy-settings).
- **Where they show.** `GET /api/sites` gives each site's `legal`: its own addresses, or else its tenant's, and
  nothing when neither sets them. The platform site, the home page and Studio take the tenant's. The realm sync writes
  the identity provider's copy on each client, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step):
  `axis.privacyUrl` and `axis.termsUrl` (the site's, or else the tenant's), `axis.licencesUrl` (the tenant host's
  `/licences`), and `axis.tenantPrivacyUrl` and `axis.tenantTermsUrl` on every client, for the terms step.

## Legal line

- **One line in two places.** Every page of the identity provider ends in it, under the card and as wide as the card,
  and so does the shell's footer, see [layout](shell.md#layout).
- **Copyright at the start, links at the end.** The line starts with the copyright in `textMuted`. At its end sit the
  links, in `primaryText` as links: Privacy and Terms when the site or its tenant sets them, then Licences, always. At
  narrow width the links wrap and stay at the end. The links sit in a navigation landmark named Legal.
- **Labels.** Privacy, Terms and Licences, in Vietnamese Quyền riêng tư, Điều khoản and Giấy phép. The page that
  Licences opens keeps its full title.

## Open source licences

- **`/licences` lists what Axis ships.** The page opens without signing in. It first names Axis's licence, the AGPL,
  with its text and a link to Axis's source, which
  [D120](../decisions/d120-axis-is-agpl-licensed-with-a-commercial-licence.md) requires Axis to offer every person
  who uses it over a network. Then it lists every npm package of the SPA and every NuGet package of the server, the
  worker and `Axis.Admin`, each with its licence, in two lists side by side, one above the other at narrow width.
- **Versions only for a signed-in person.** A signed-in person also sees each package's version, and the source link
  points at the exact version the server runs. An anonymous visitor sees neither, for the reason the shell's footer
  hides the version: it tells an attacker which known weaknesses to try, see [layout](shell.md#layout). A search box filters both by name or licence. Each entry opens its licence text, or links the standard text on
  spdx.org.
- **Built from what ships.** The build writes the list from the packages that ship, so it never falls behind them,
  and the SPA serves it. No list is kept by hand.
- **A reserved path.** `licences` joins the reserved site paths, so no site can take `/licences`, see
  [sites and pages](configuration.md).

## Accepting the terms

- **Once, then on each new version.** After a person's first sign-in, and again when the tenant's terms change
  version, the identity provider asks them to accept the terms in a step of its own, never as a box on every sign-in.
  It is Keycloak's Terms and Conditions required action, drawn as an Axis page.
- **The tenant's terms.** The step asks for the tenant's terms and privacy policy, whichever client signs the person
  in, because the identity provider asks it of the whole realm. A site's own addresses change only its legal line.
- **One required tick.** The page shows one checkbox that names the Terms of service and the Privacy policy as links,
  Continue, and Decline and sign out. Continue with the box unticked marks the box with an error. Continue records
  when the person accepted. Declining ends the sign-in, and Axis's sign-in card says that signing in needs the terms
  accepted.
- **On only while there are terms.** The realm sync turns the step on while the tenant sets terms and off when it has
  none, so the page never shows without a link. When the tenant's terms version changes, the sync asks it again of
  every user of the realm.
