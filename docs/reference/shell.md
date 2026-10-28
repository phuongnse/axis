# Shell and home page

The shell is the frame the SPA draws around every page of every site, the platform site and application sites
alike: a header, the site's navigation, the page's header and a footer. The platform home page is the first page a
person sees, and the sign-in screen when nobody is signed in. This page holds their rules. The routes, the site
endpoints and the widgets are in [frontend](frontend.md), and sign-in and the session in
[security](security.md#sign-in-and-the-session).

[D98](../decisions/d98-rules-from-the-shell-review.md) agrees everything on this page. Only the
[navigation icons](#navigation-icons) list, its check, the API field and the SPA map are built. M16 builds the rest, so
every other section is _(planned for M16)_. Until it is built, the shell is as [frontend](frontend.md) describes it: one header
with the site's title, its navigation as a horizontal menu, the notification bell, the account button, the locale
switch and the theme toggle.

## Layout

_(planned for M16)_

- **Header.** One row, 48 px high, on `surface` with a `borderSubtle` rule below it. From the left: the button that
  collapses the sidebar, the site's mark, the site's title, then on the right the notification bell, the app switcher,
  the language and theme menu and the account button. The mark is the first letter of the site's title on a
  `primary` tile with `radius-base` corners. The mark and the title are one link to the site's first navigation page.
  The header's items are centred by its flex row, never by its line height.
- **Sidebar.** The site's navigation entries, in configuration order, in a sidebar 224 px wide on `surface` with a
  `borderSubtle` rule on its right. Each entry shows its icon, see [navigation icons](#navigation-icons), and its
  label. The entry of the current page is drawn in `primaryText` on a `primary` tint and marked `aria-current`.
- **Collapsed sidebar.** The header's button collapses the sidebar to a strip 64 px wide of the entries' icons, each
  named by a tooltip, and opens it again. The button names what it does, "Collapse navigation" or "Expand
  navigation", and has `aria-expanded`. The sidebar stays open or collapsed while the person moves between pages.
- **One-page sites.** A site with one navigation entry, such as the platform site, has no sidebar and no button to
  collapse it. The page takes the whole width.
- **Narrow width.** Below 992 px the sidebar gives way to a panel under the header, which a Menu button opens and
  closes, as it does today.
- **Page area.** Pages draw on `canvas` with `spacing-lg` padding. A wide table scrolls inside its widget, so the
  page never scrolls sideways.
- **Footer.** A slim footer runs the full width under the sidebar and the page, as the header does above them, on
  `surface` with a `borderSubtle` rule above it. On the left it reads "© <year> Axis", then " · v<version>" for a
  signed-in person only, as in "© 2026 Axis · v0.1.0". On the right it shows the server's readiness, "Server status"
  and a dot with Ready, Checking or Unavailable, so everyone sees when the server is unavailable. An anonymous visitor
  never sees the version, because it tells an attacker which known weaknesses to try.

## Navigation icons

- A navigation entry of a `site` resource may have an `icon`: one name from a closed list that Axis owns, such as
  `home`, `dashboard`, `list`, `inbox`, `tasks`, `form`, `document`, `folder`, `chart`, `process`, `people`,
  `person`, `calendar`, `clock`, `money`, `cart`, `tag`, `device`, `tool`, `support`, `mail` and `settings`. The
  configuration check refuses a name outside the list.
- The names are Axis's, never the UI library's. The SPA maps each name to one outlined icon, so replacing the library
  changes the map and no application, as [D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)
  requires.
- The map gives an entry with no icon a neutral page icon. An icon is `aria-hidden`, as the label names the entry.
- `GET /api/sites/{path}` gives each navigation item its `icon`, or `null`. `GET /api/site` gives the platform
  site's Home entry the `home` icon.

## Page header

_(planned for M16)_

- Every page of a site starts with a header band on `surface` across the page area, with a `borderSubtle` rule below
  it, over the page's widgets on `canvas`.
- The band holds the breadcrumb, then the page title in `heading4`. The breadcrumb starts at the site and ends at the
  page: "Purchase requests › Requests". A form page opened from a table names that table's navigation page between
  them, "Purchase requests › Requests › New request", and its link returns to the table with its paging and sorting.
  Every step but the last is a link, and the last is marked `aria-current`.
- A widget's actions stay in its own toolbar, because a page can hold several widgets and an action belongs to its
  widget, not to the page.

## App switcher and preferences

_(planned for M16)_

- **App switcher.** An icon button named "Switch to" opens a menu of the person's sites, by title, then the tools of
  their roles: Studio for an author and the operator console for an operator. It replaces the Studio link beside the
  account button. With nobody signed in it is not shown.
- **Language and theme.** An icon button named "Language and theme" opens one menu with the site's languages and the
  light and dark themes, the current ones selected. It replaces the locale switch and the theme toggle in the header,
  for signed-in people and for nobody signed in alike.

## Platform home page

_(planned for M16)_

- **Signed in.** The page goes from the highest level to the lowest.
  - A welcome band on `surface`: "Welcome to Axis" and "Open an application below to start working.", and under them
    the tools of the person's roles, Studio for an author ("Build and publish applications.") and the operator console
    for an operator, each a tile with its icon beside its title and description, aligned to the top.
  - "Applications" with their number, then one card per application across the page, in title order: the
    application's mark, its title and the number of its sites, over its sites. Each site is a row with its mark and
    title that opens the site, with no icon after it.
  - Tools and sites share one grid of three columns, so their columns line up. Below 992 px it is one column.
- **Signed out.** The home page is the sign-in screen: one card in the middle of the page with the Axis mark, "Sign in
  to Axis", "You sign in at your organization's sign-in page, then come back here." and a full-width Sign in button.
  The header shows no second Sign in button there. Nothing starts sign-in until the person clicks it.
- **Refused sign-in.** `/?signIn=failed` shows the same card, with the warning "Sign-in did not complete. Try again."
  above the button, with no reason.
- **Endpoints.** These three are built, while the page that uses them stays planned.
  - `GET /api/sites` gives each site its `application`, `{ "name", "titles" }`, the application's name and its title
    per locale it has texts for, from the `label` in its `application.json` or its name when it has none, so the home page can group the sites.
  - `GET /api/me` adds a boolean `console`, true when the person holds the role `Console:OperatorRole` names, see the
    [operator console](operator-console.md).
  - `GET /api/about` answers a signed-in person with `{ "version" }`, the server's informational version, the one the
    images carry. Without a session it answers `401`, like every other `/api` path that needs one.

## Session ended

_(planned for M16)_

- When a request answers `401` after sign-in, such as once the session has ended at the re-authentication Axis asks
  for every 8 hours, the sign-in dialog is titled "Your session has ended" and reads "Sign in again to continue." It
  cannot be dismissed, and only its Sign in button starts sign-in.

## Design system previews

_(planned for M16)_

- The design system has a seventh group of cards, Shell, after Shared states, with three previews:
  - `shell`: Application site, Navigation collapsed, Form page and Platform site.
  - `signIn`: Signed out, Sign-in refused and Session ended.
  - `identitySignIn`: the sign-in page of [Axis's identity provider](identity-provider.md#theme), in Sign in, Wrong
    password, Organization sign-in only and Two-step verification.
- A shell preview draws the shell in a frame of its own height, so several states fit one card.
- See [widget previews](../development.md#widget-previews).
