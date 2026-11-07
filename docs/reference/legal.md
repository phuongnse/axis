# Legal links and terms

This page describes the links to an organization's privacy policy and terms of service, the legal line
that shows them, the page that lists the licences of what Axis ships, and the step where a person
accepts the terms.

## Legal settings

An organization sets the addresses of its own policies, and Axis shows them wherever its people sign
in or work.

- **The organization owns its policies.** A privacy policy and terms of service are the organization's, because it
  decides what it collects about its people and on what terms. In Axis SaaS the organization is the controller and Axis
  the processor. Axis writes no
  policy text and ships no default one: it only links what the organization publishes.
- **Set per tenant, with a site override.** A tenant sets the `https` addresses of its privacy policy and its terms,
  and the version of its terms. A site may set its own two addresses, which replace the tenant's on that site's legal
  line only. The commands are in [legal links](tenant-setting-commands.md#legal-links). Under the `configuration`
  tenancy source, the tenant's `Legal` block holds the same values, see
  [tenancy settings](host-settings.md#tenancy-settings).
- **Where they show.** `GET /api/sites` gives each site's `legal`, written `{ "privacyUrl", "termsUrl" }`: its own
  addresses when it sets either, else its tenant's, and `null` when neither sets any. A site that sets either address
  uses only its own pair. The platform site, the home page and Studio take the tenant's: `GET /api/site` gives the
  tenant's `legal`, and `GET /api/sites/{path}` gives a site's `legal` by the same rule as the list. The realm
  sync writes the identity provider's copy on each client, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step):
  `axis.privacyUrl` and `axis.termsUrl` (the site's, or else the tenant's), `axis.licencesUrl` (`/licences` on the
  tenant's first host that belongs to no site, or else its first host), and `axis.tenantPrivacyUrl` and
  `axis.tenantTermsUrl` on every client, for the terms step. A link that is not set is removed from the clients.

## Legal line

The legal line closes every page with the copyright and the legal links.

- **One line in two places.** Every page of the identity provider ends in it, under the card and as wide as the card,
  and so does the shell's footer, see [layout](shell.md#layout).
- **Copyright at the start, links at the end.** The line starts with the copyright in `textMuted`. At its end sit the
  links, in `primaryText` as links: Privacy and Terms when the site or its tenant sets them, then Licences, always. At
  narrow width the links wrap and stay at the end. The links sit in a navigation landmark named Legal.
- **Labels.** Privacy, Terms and Licences, in Vietnamese Quyền riêng tư, Điều khoản and Giấy phép. The landmark's name is Legal, in Vietnamese Pháp lý. The page that
  Licences opens keeps its full title.

## Open source licences

Axis lists the licence of everything it ships on a page that anyone can open.

| Route               | What it does                                                                                            |
| ------------------- | ------------------------------------------------------------------------------------------------------- |
| `GET /api/licences` | The server-side licence file, for a signed-in person, see [open source licences](#open-source-licences) |

- **`/licences` lists what Axis ships.** The page opens without signing in. It first names Axis's licence, the AGPL,
  with its text and a link to Axis's source, which the AGPL requires Axis to offer every person who uses it over a
  network. Then it lists every npm package of the SPA and every NuGet package of the server, the
  worker and `Axis.Admin`, each with its licence, in two lists side by side, one above the other at narrow width.
- **Versions only for a signed-in person.** A signed-in person also sees each package's version, and the source link
  points at the commit when the version the server runs names one (build metadata of 7 to 40 hex characters after
  `+`), else at the repository. An anonymous visitor sees neither, for the reason the shell's footer hides the
  version: it tells an attacker which known weaknesses to try, see [layout](shell.md#layout). A search box filters
  both by name or licence. A licence that is one SPDX identifier links its text on spdx.org, and one that is an
  `https` address links that address. Any other licence, such as an expression, a file name or `UNKNOWN`, shows as
  plain text.
- **Built from what ships.** The build writes the list from the packages that ship, so it never falls behind them,
  and the SPA serves it. No list is kept by hand. `scripts/build.sh` runs `scripts/write-licences.mjs`, which writes
  the public `/licences.json`, with names and licences and no versions, and a server-side `licences.json` that also
  holds versions. `GET /api/licences` gives the server-side file only to a signed-in person and answers `401` without a
  session.
- **A reserved path.** `licences` joins the reserved site paths, so no site can take `/licences`, see
  [sites and pages](configuration.md).

## Accepting the terms

A person accepts the tenant's terms in a step of the identity provider, once and again on each new version.

- **Once, then on each new version.** After a person's first sign-in, and again when the tenant's terms change
  version, the identity provider asks them to accept the terms in a step of its own, never as a box on every sign-in.
  It is Keycloak's Terms and Conditions required action, drawn as an Axis page.
- **The tenant's terms.** The step asks for the tenant's terms and privacy policy, whichever client signs the person
  in, because the identity provider asks it of the whole realm. A site's own addresses change only its legal line.
- **One required tick.** The page shows one checkbox that names the Terms of service and the Privacy policy as links,
  Continue, and Decline and sign out. The links open in a new tab, so the person keeps the step. A tenant that sets
  terms but no privacy policy gets a label that names only the Terms of service. Continue with the box unticked marks
  the box with an error and sends nothing. Continue records when the person accepted. Declining ends the sign-in at
  `/?signIn=declined`, Axis's sign-in card, which says that signing in needs the terms accepted, see
  [the shell's sign-in card](shell.md#the-sign-in-card).
- **On only while there are terms.** The realm sync turns the step on, for new users too, while the tenant sets terms
  and off when it has none, so the page never shows without a link. When the tenant's terms version changes, the
  sync asks it again of every user of the realm, and then stores the version in the realm attribute
  `axis.termsVersion`. When the tenant has no terms the sync clears that attribute, so terms set later ask everyone.
