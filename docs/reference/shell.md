# Shell and home page

The shell is the frame the SPA draws around every page of every site, the platform site and application sites
alike: a header, the site's navigation, the page's header and a footer. The platform home page is the first page a
person sees, and the sign-in screen when nobody is signed in. This page holds their rules. The routes, the site
endpoints and the widgets are in [frontend](frontend.md), and sign-in and the session in
[security](security.md#sign-in-and-the-session).

[D98](../decisions/d98-rules-from-the-shell-review.md) agrees everything on this page. The [layout](#layout) and the
[navigation icons](#navigation-icons), the [page header](#page-header), the
[app switcher and preferences](#app-switcher-and-preferences), the [platform home page](#platform-home-page) and
[session ended](#session-ended) are built, and so are the design system's shell previews and their Shell group of
cards.

## Layout

- **Header.** One row, 48 px high, on `surface` with a `borderSubtle` rule below it. From the left: the button that
  collapses the sidebar, the site's mark, the site's title, then on the right the notification bell, the app switcher,
  the language and theme menu and the account button. The mark is the first letter of the site's title on a
  `primary` tile with `radius-base` corners. The mark and the title are one link to the site's first navigation page.
  The header's items are centred by its flex row, never by its line height. The header has
  `spacing-md` padding at its sides, and the items on its right are `spacing-sm` apart. The collapse button, the app
  switcher and the language and theme button are icon-only buttons, so each shows its name as a tooltip on hover and
  on keyboard focus, see [buttons](frontend.md).
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
  never sees the version, because it tells an attacker which known weaknesses to try. The footer
  has `spacing-xs` padding above and below and `spacing-lg` at its sides. Its text is in `textMuted`, and the
  readiness reads "Server status:" with the dot and the word after it. In a design system preview, each state's
  frame shows the whole shell, from the header to the footer.

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

- Every page of a site starts with a header band on `surface` across the page area, with a `borderSubtle` rule below
  it, over the page's widgets on `canvas`.
- The band holds the breadcrumb, then the page title in `heading4`. It has `spacing-lg` padding
  above and at its sides, `spacing-md` below the title, and `spacing-xs` between the breadcrumb and the title. The breadcrumb starts at the site and ends at the
  page: "Purchase requests › Requests". A form page opened from a table names that table's navigation page between
  them, "Purchase requests › Requests › New request", and its link returns to the table with its paging and sorting.
  Every step but the last is a link, and the last is marked `aria-current`.
- A widget's actions stay in its own toolbar, because a page can hold several widgets and an action belongs to its
  widget, not to the page.
- The platform home page has no band and no breadcrumb. It has its own welcome band, see the
  [platform home page](#platform-home-page). The platform's Sessions page has the band, "Axis › Your sessions". The users page has the band, "Axis › Users".

## App switcher and preferences

- **App switcher.** An icon button named "Switch to" opens a menu of the person's sites, by title, then the tools of
  their roles: Studio for an author, the operator console for an operator and Users for an administrator. It replaces the Studio link beside the
  account button. With nobody signed in it is not shown. The menu shows the sites under the
  heading "Sites" and the tools under the heading "Tools".
- **Language and theme.** An icon button named "Language and theme" opens one menu with the site's languages and the
  light and dark themes, the current ones selected. Its icon is a settings gear. The current
  language and theme are drawn selected, in `primaryText` on a `primary` tint, not only marked for assistive
  technology, and a divider separates the languages from the themes. It replaces the locale switch and the theme toggle in the header,
  for signed-in people and for nobody signed in alike. At narrow width it sits in the Menu panel, while the app
  switcher stays in the header.
- **Users.** The app switcher and the home page's welcome band also offer "Users" to an administrator, after the
  operator console, see [the administrator role](user-administration.md#the-administrator-role). The users page it
  links to is `/users`. On a site's own host the app switcher lists only that site and no tools.

## Platform home page

- **Signed in.** The page goes from the highest level to the lowest.
  - A welcome band on `surface`: "Welcome to Axis" and "Open an application below to start working.", and under them
    the tools of the person's roles, Studio for an author ("Build and publish applications."), the operator console
    for an operator ("See what failed and run the commands that fix it.") and Users for an administrator ("Invite
    people and give them roles."), each a tile with its icon beside its title
    and description, aligned to the top. The band is a panel inside the page area, on `surface`
    with a `borderSubtle` border, `radius-base` corners and `spacing-lg` padding, not a band across the page. Each
    tool tile is a panel too, with `spacing-md` padding, and its icon is white on a 32 px `primary` square.
  - "Applications" with their number, then one card per application across the page, in title order: the
    application's mark, its title and the number of its sites, over its sites. Each site is a row with its mark and
    title that opens the site, with no icon after it. The number of applications is plain text in
    `textMuted`. Each card is a panel like the welcome band, with `spacing-md` padding. Its header row holds the
    application's 32 px mark, its title in `heading5` and, on the right, the number of its sites in `textMuted`, with
    a `divider` rule under it. A site row has `spacing-xs` padding, its 24 px mark and its title in `text`.
  - Tools and sites share one grid of three columns, so their columns line up. Below 992 px it is one column.
- **Signed out.** The home page is the sign-in screen: one card in the middle of the page with the Axis mark, "Sign in
  to Axis", "You sign in at your organization's sign-in page, then come back here." and a full-width Sign in button.
  The card is a panel like the welcome band, at most 420 px wide, with `spacing-xl` padding, a
  48 px mark and the explanation in `textMuted`. The header shows no second Sign in button there. Nothing starts sign-in until the person clicks it. The page shows
  the card when `GET /api/me` or `GET /api/sites` answers `401`.
- **Refused sign-in.** `/?signIn=failed` shows the same card, with the warning "Sign-in did not complete. Try again."
  above the button, with no reason.
- **Endpoints.** The page uses these three.
  - `GET /api/sites` gives each site its `application`, `{ "name", "titles" }`, the application's name and its title
    per locale it has texts for, from the `label` in its `application.json` or its name when it has none, so the home page can group the sites.
  - `GET /api/me` adds a boolean `console`, true when the person holds the role `Console:OperatorRole` names, see the
    [operator console](operator-console.md).
  - `GET /api/me` adds a boolean `users`, true when the person holds the role `Users:AdministratorRole` names and the
    tenant is not a preview tenant, see [users settings](host-settings.md#users-settings).
  - `GET /api/about` answers a signed-in person with `{ "version" }`, the server's informational version, the one the
    images carry. Without a session it answers `401`, like every other `/api` path that needs one.

## Session ended

- When a request answers `401` after sign-in, such as once the session has ended at the re-authentication Axis asks
  for every 8 hours, the sign-in dialog is titled "Your session has ended" and reads "Sign in again to continue." It
  cannot be dismissed, and only its Sign in button starts sign-in.

## Design system previews

- The design system has a seventh group of cards, Shell, after Shared states, with three previews:
  - `shell`: Application site, Navigation collapsed, Form page and Platform site.
  - `signIn`: Signed out, Sign-in refused and Session ended.
  - `identitySignIn`: the sign-in page of [Axis's identity provider](identity-provider.md#theme), in Sign in, Wrong
    password, Organization sign-in only, Two-step verification and Add an authenticator. It is built from fixtures through the shared sign-in components in `web/src/identitySignIn/`. Two-step
    verification and Add an authenticator are the same components that the theme draws.
- A shell preview draws the shell in a frame at least as tall as its height, which grows to hold the whole shell, so
  several states fit one card.
- See [widget previews](../development.md#widget-previews).
