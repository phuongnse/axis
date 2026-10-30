# Shell and home page

The shell is the frame the SPA draws around every page of every site, the platform site and application sites
alike: a header, the site's navigation, the page's header and a footer. The platform home page is the first page a
person sees, and the sign-in screen when nobody is signed in. This page holds their rules. The routes, the site
endpoints and the widgets are in [frontend](frontend.md), and sign-in and the session in
[security](security.md#sign-in-and-the-session).

[D98](../decisions/d98-rules-from-the-shell-review.md) agrees this page, and
[D114](../decisions/d114-rules-from-the-sign-in-and-home-page-review.md) changes some of its rules: the organization
heading on the sign-in card, sign-in that starts at once, the shell that fills the window, the menus, the theme that
follows the system and the home page. D98's rules are built, and so are the design system's shell previews and their
Shell group of cards. D114's rules are marked _(planned for M17)_ until they are built; each such mark says what the
SPA does today.

## Layout

- **Header.** One row, 48 px high, on `surface` with a `borderSubtle` rule below it. From the left: the button that
  collapses the sidebar, the site's mark, the site's title, then on the right the notification bell, the app switcher,
  the language and theme menu and the account button. The mark is the first letter of the site's title on a
  `primary` tile with `radius-base` corners. The mark and the title are one link to the site's first navigation page.
  The header's items are centred by its flex row, never by its line height. The header has
  `spacing-md` padding at its sides, and the items on its right are `spacing-sm` apart. The collapse button, the app
  switcher and the language and theme button are icon-only buttons, so each shows its name as a tooltip on hover and
  on keyboard focus, see [buttons](frontend.md). _(Planned for M17.)_ The platform site's title and mark name the
  organization once the tenant has a name, and Axis until then, as the
  [sign-in card](#platform-home-page) does.
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
- **Window.** _(Planned for M17.)_ The shell fills the browser window: the page has no margin round it, and the
  document behind it is `canvas` in both modes, so no white shows at its edges or when the page scrolls past its
  end. Today the browser's default 8 px margin shows round the shell.
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
- The platform home page has no band and no breadcrumb. It has its own headings, see the
  [platform home page](#platform-home-page). The platform's Sessions page has the band, "Axis › Your sessions". The users page has the band, "Axis › Users".

## App switcher and preferences

- **App switcher.** An icon button named "Switch to" opens a menu of the person's sites, by title, then the tools of
  their roles: Studio for an author, the operator console for an operator and Users for an administrator. It replaces the Studio link beside the
  account button. With nobody signed in it is not shown. The menu shows the sites under the
  heading "Sites" and the tools under the heading "Tools". _(Planned for M17.)_ The entry of the current page, a site
  or a tool, is drawn selected, as the language and theme menu draws its current choices, and marked
  `aria-current="page"`. Today no entry is marked.
- **Menus.** _(Planned for M17.)_ Every dropdown menu keeps `spacing-xs` round its items, so a selected last item does
  not sit on the menu's edge. Today the menus keep 4 px.
- **Language and theme.** An icon button named "Language and theme" opens one menu with the site's languages and the
  themes, the current ones selected. _(Planned for M17.)_ The themes are System, Light and Dark. System is the default,
  when the browser holds no choice, and follows the operating system's light or dark setting, changing at once when
  it changes. Light and Dark keep their mode. The choice is kept in the browser under `axis.themeMode`, as `system`,
  `light` or `dark`. Today the menu offers Light and Dark, and the system's setting applies only until one is picked. Its icon is a settings gear. The current
  language and theme are drawn selected, in `primaryText` on a `primary` tint, not only marked for assistive
  technology, and a divider separates the languages from the themes. It replaces the locale switch and the theme toggle in the header,
  for signed-in people and for nobody signed in alike. At narrow width it sits in the Menu panel, while the app
  switcher stays in the header.
- **Users.** The app switcher and the home page's tools also offer "Users" to an administrator, after the
  operator console, see [the administrator role](user-administration.md#the-administrator-role). The users page it
  links to is `/users`. On a site's own host the app switcher lists only that site and no tools.

## Platform home page

_(The page as [D114](../decisions/d114-rules-from-the-sign-in-and-home-page-review.md) agrees it is planned for M17.
Today it shows D98's welcome band with the tools, then a card per application across the page with the number of
applications and of its sites, and the sign-in screen with nobody signed in.)_

- **Signed in.** The page has two parts, each under a `heading5` title with no count beside it: "Applications", then
  "Tools", as people use the applications most.
  - Each application is one tile across the page, in title order: its 32 px mark, the first letter of its title on a
    `primary` tile, its title, the `label` of its `application.json` or its name when it has none, and its
    `description` in `textMuted`, what the application is for as a whole. Under a `divider` rule follow all its sites,
    even a lone one, in title order: each a link with the site's 20 px mark, its title in `primaryText` and, under it
    in `textMuted`, the site's own `description`, what that site does. See
    [descriptions](#application-and-site-descriptions). An application with many sites does not push
    the others down, because it has the whole row to itself.
  - The tools of the person's roles follow as tiles: Studio for an author ("Build and publish applications."), the
    operator console for an operator ("See what failed and run the commands that fix it.") and Users for an
    administrator ("Invite people and give them roles."). Each tile is its icon, white on a 32 px `primary` square, its
    title and its description, aligned to the top. The whole tile is the link.
  - A tile is a panel on `surface` with a `borderSubtle` border, `radius-base` corners and `spacing-md` padding, its
    mark and its text `spacing-sm` apart. A title with no description under it is centred on its mark. Applications are
    `spacing-md` apart.
  - A tile that is a link shows it only by its title in `primaryText`, as every link: hover changes nothing, and
    keyboard focus draws the `focusRing` outline, 2 px wide and 2 px out.
  - The tools, and the sites inside an application, sit in the home grid, which takes as many columns as it has tiles,
    up to three: one tile fills the row, two share it, and three or more take a third each, the fourth starting the
    next row. Its columns are `repeat(auto-fit, minmax(max(0px, calc((100% - 2 × spacing-md) / 3)), 1fr))` with a
    `spacing-md` gap, so no code counts the tiles. Below 992 px it is one column.
- **Signed out.** A person who opens Axis without a session goes straight to the sign-in page of
  [Axis's identity provider](identity-provider.md#theme). Axis draws no screen of its own in front of it. The page
  starts sign-in when `GET /api/me` or `GET /api/sites` answers `401`.
- **The sign-in card.** Axis shows a card only where going straight to sign-in would loop or surprise. It has the
  identity provider's frame: the organization's 32 px mark and name above one card in the middle of the page, or Axis
  while the organization is not known, see [theme](identity-provider.md#theme). A tenant has no name until
  `tenant create` takes one, see [realms](identity-provider.md#realms). The card is at most 400 px wide, on
  `surface` with a `borderSubtle` border, `radius-base` corners and `spacing-xl` padding. The header shows no second
  Sign in button there, and nothing starts sign-in until the person clicks the card's button.
  - **Refused sign-in.** `/?signIn=failed` shows "Sign in", the warning "Sign-in did not complete. Try again." with no
    reason, and a full-width Sign in button. Starting sign-in at once there would loop, as the identity provider's
    session is still open.
  - **Signed out.** After sign-out the identity provider returns the browser to `/?signedOut`, which shows "You have
    signed out", "You can close this page, or sign in again." in `textMuted` and a full-width "Sign in again" button.
  - The button returns to `/` after sign-in, never to either address.
- **Endpoints.** The page uses these three.
  - `GET /api/sites` gives each site its `application`, `{ "name", "titles", "descriptions" }`, the application's name
    and its title and description per locale it has texts for, from the `label` and `description` in its
    `application.json`, its name when it has no label and `null` descriptions when it has no description, so the home
    page can group the sites. Each site also gets its `descriptions` per available locale, or `null`. It lists only the
    sites that allow the provider of the session, so a person signed in through a provider that one site allows sees
    that site only.
  - `GET /api/me` adds a boolean `console`, true when the person holds the role `Console:OperatorRole` names, see the
    [operator console](operator-console.md).
  - `GET /api/me` adds a boolean `users`, true when the person holds the role `Users:AdministratorRole` names and the
    tenant is not a preview tenant, see [users settings](host-settings.md#users-settings).
  - `GET /api/about` answers a signed-in person with `{ "version" }`, the server's informational version, the one the
    images carry. Without a session it answers `401`, like every other `/api` path that needs one.

## Application and site descriptions

_(Planned for M17, [D114](../decisions/d114-rules-from-the-sign-in-and-home-page-review.md).)_ The `application`
manifest and each `site` may have a `description`, one line of text that the
[platform home page](#platform-home-page) shows under the title:

```json
{
  "id": "219102bc-864f-42ed-874a-c19eaee0d1ba",
  "kind": "application",
  "name": "PurchaseRequests",
  "formatVersion": 1,
  "label": { "textKey": "application.title" },
  "description": { "textKey": "application.description" }
}
```

```json
{
  "kind": "site",
  "name": "Purchasing",
  "title": { "textKey": "site.title" },
  "description": { "textKey": "site.description" }
}
```

- The application's `description` says what the application is for as a whole, such as "Runs the company's
  purchases, from the request to approval and ordering." A site's says what that site does and who it is for, such
  as "Request purchases and take them through approval.", as an application often has several sites for different
  people. The two should not repeat each other.
- Both are optional `{ "textKey" }` texts, checked like the application's `label` and the site's `title`: a key that
  no text resource holds is `AXC0028`, at `/description`. Without one, the home page shows the title alone.
- The application's `label` is its title on the home page and in the app switcher, and its name when it has none, so
  a sample or template gives its application a `label` in every locale it has texts for.
- `GET /api/sites` gives both descriptions per locale, see [platform home page](#platform-home-page).

## Session ended

- When a request answers `401` after sign-in, such as once the session has ended at the re-authentication Axis asks
  for every 8 hours, the sign-in dialog is titled "Your session has ended" and reads "Sign in again to continue." It
  cannot be dismissed, and only its Sign in button starts sign-in.

## Design system previews

- The design system has a seventh group of cards, Shell, after Shared states, with three previews:
  - `shell`: Application site, Navigation collapsed, Form page and Platform site.
  - `signIn`: Signed out, Sign-in refused and Session ended. _(Planned for M17: Sign-in refused, Signed out and Session
    ended, with the organization's heading over the cards.)_
  - `identitySignIn`: the sign-in page of [Axis's identity provider](identity-provider.md#theme), in Sign in, Wrong
    password, Organization sign-in only, Two-step verification, Forgot password, Set password and Add an authenticator. It is built from fixtures through the shared sign-in components in `web/src/identitySignIn/`. Two-step
    verification, Forgot password, Set password and Add an authenticator are the same components that the theme draws.
- A shell preview draws the shell in a frame at least as tall as its height, which grows to hold the whole shell, so
  several states fit one card.
- See [widget previews](../development.md#widget-previews).
