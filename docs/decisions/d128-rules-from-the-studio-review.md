# D128. Rules from the Studio review

Status: **Agreed**

The owner reviewed Studio, the shell, the shared controls and every page and email of Axis's identity provider in the
Design System artifact, one change at a time, and agreed these rules between 2026-10-30 and 2026-11-01. They change
some rules of [D96](d96-rules-from-the-design-system-review.md) (the button levels),
[D98](d98-rules-from-the-shell-review.md) (the footer) and [D114](d114-rules-from-the-sign-in-and-home-page-review.md)
(how sign-out reaches `/?signedOut`). None of it is built yet _(planned for M31)_. The prototype is the tag
`design-review/2026-10-30-studio`.

- **An open application is a workspace.** Studio lists the applications as the home page lists tools: tiles by title
  and description, up to three columns, with New application… as the main action. An open application takes the
  shell's sidebar for its own navigation and draws its page compact. `GET /api/studio/apps` gives each application's
  titles and descriptions per locale. See [Studio workspace](../reference/studio-workspace.md).
- **The band names the kind and the title.** Every Studio page has one 48 px band: the kind as a link to its list, the
  title, then the actions. Validate, Preview and Run tests are text buttons, so Publish… is the band's one solid
  button. The application menu leads back to every application. See
  [band](../reference/studio-workspace.md#band).
- **The Overview says what to do next.** It shows no counts: a get-started list for an application with no entity, the
  diagnostics that need attention with errors first, the live release and whether changes are not live yet, and each
  kind with no resource yet as a link with one line on what it is for. See
  [Overview](../reference/studio-workspace.md#overview).

- **The page keeps the browser's own scroll.** The header, a sidebar's navigation and a compact band stay in place
  while the page scrolls, and every scrollbar follows the mode. See [layout](../reference/shell.md#layout).
- **Sidebars show text or icons.** An open sidebar entry is its label alone, a collapsed one its icon alone with a
  tooltip, and no entry shows a count. See [layout](../reference/shell.md#layout).
- **The account button shows who is signed in.** The person's avatar, their name and a caret. See
  [layout](../reference/shell.md#layout).
- **The footer drops the server status.** It read the server once, when the page loaded, and a page that cannot reach
  the server already says so where it fails. This changes D98's footer, which is now the copyright, the version for a
  signed-in person and the legal links. See [layout](../reference/shell.md#layout).
- **Tiles in a grid share one height.** A description takes at most two lines, with the whole text in a tooltip. See
  [platform home page](../reference/shell.md#platform-home-page).

- **An action has text or an icon, never both.** An icon goes with text only where it does what text cannot:
  navigation that collapses to icons, a status or a severity, and the mark of an application or a site. See
  [actions and controls](../reference/actions-and-controls.md#buttons).
- **Beside a solid button every other action is a text button.** One rule by position, in dialogs, form footers and
  bands, replaces D96's outlined main and grey secondary levels. See
  [buttons](../reference/actions-and-controls.md#buttons).
- **An action that asks for more ends in an ellipsis,** and a text trigger that opens a menu ends in a caret. See
  [buttons](../reference/actions-and-controls.md#buttons).
- **One form setting places every field error.** The message sits under its control and the field keeps its gap below
  it. A checkbox or a radio keeps its box on its label's first line. A control with an error takes `dangerText` for its
  border, so it keeps 3:1 in dark mode. See
  [fields](../reference/actions-and-controls.md#fields).

- **Every page and email is Axis's.** No page of the identity provider keeps Keycloak's look: the sign-out question,
  notices, errors, the expired page, the choice of method and the profile steps join the sign-in pages. Every email
  takes the Axis email theme, in English and Vietnamese, and `tenant create` sets it. See
  [theme](../reference/identity-provider.md#theme).
- **Language and theme belong to the page.** The shell's gear button sits at the page's top right and opens the
  shell's language and theme menu, so another language only adds a row. See
  [theme](../reference/identity-provider.md#theme).
- **Sign-out asks nothing.** Axis keeps the identity provider's session id (`sid`) with its session, and on sign-out
  the server ends that session through the identity provider's admin API, then returns the browser to `/?signedOut`.
  The `sid` is not a token, so [D9](d9-authentication-oidc-through-a-bff-ready-for-fapi-20.md) holds. See
  [sign-in and the session](../reference/security.md#sign-in-and-the-session).
- **Axis's account page replaces Keycloak's account console.** Change password and Authenticators start the identity
  provider's own steps, whose pages are Axis pages, and `tenant create` turns the account console off. See
  [account page](../reference/platform-pages.md#account-page).

- **The organization owns its policies.** A tenant sets the addresses of its privacy policy and terms of service, and
  a site may set its own. Axis writes no policy text. See [legal settings](../reference/legal.md#legal-settings).
- **One legal line ends every sign-in page and the shell's footer.** The copyright at the start, then Privacy and Terms
  when set and Licences, always, at the end. See [legal line](../reference/legal.md#legal-line).
- **`/licences` lists what Axis ships.** The page names Axis's AGPL licence and links its source, as
  [D120](d120-axis-is-agpl-licensed-with-a-commercial-licence.md) requires, then every library with its licence. It
  is built from what ships and opens without signing in, and `licences` becomes a reserved site path. See
  [open source licences](../reference/legal.md#open-source-licences).
- **A person accepts the tenant's terms once.** After the first sign-in, and again when the tenant's terms change
  version, a step of its own asks for one required tick. Declining signs the person out. See
  [accepting the terms](../reference/legal.md#accepting-the-terms).
