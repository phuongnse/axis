# Shell and home page

The shell is the frame the SPA draws around every page of every site, the platform site and
application sites alike: a header, the site's navigation, the page's header and a footer. The platform
home page is the first page a person sees, and a person with no session goes to sign-in from it. This
page holds the rules of both. The routes, the site endpoints and the widgets are in
[frontend](frontend.md), and sign-in and the session in [security](security.md#sign-in-and-the-session).

## Layout

The shell's parts keep the same places on every site, so a person finds the navigation, the page and
their own controls where they expect them.

- **Header.** One row of the `bar` height, on `surface` with a `borderSubtle` rule below it. From the
  left it holds the button that collapses the sidebar, the site's mark and the site's title. On the
  right it holds the notification bell, on an application site and on the platform site for an
  administrator, then the app switcher, the language and theme menu and the account button. The
  header's items are centred by its flex row, never by its line height. The header has `spacing-md`
  padding at its sides, and the items on its right are `spacing-sm` apart.
  On the cell console's host the operator's name takes the place of the app switcher and the account
  button, see [the console's views](cell-console.md#views).
- **Read-only banner.** While the tenant is [read-only](subscriptions.md#the-read-only-tenant), a
  warning banner with the alert role sits at the top of the content, under the header and beside the
  sidebar, on every page of every site. It says that the workspace is read-only and why. An
  administrator in a cell also gets a link to the billing portal, the `billingUrl` of
  `GET /api/me`. The cell console's host has no tenant, so it shows no banner.
- **Mark and title.** The mark is the first letter of the site's title on a `primary` tile with
  `radius-base` corners, and the mark and the title are one link to the site's first navigation page.
  The platform site's title and mark name Axis, because the tenant has no name yet, as the
  [sign-in card](#the-sign-in-card) does.
- **Account button.** The account button is the person's avatar, the initials of their name on a
  `primary` circle, then their name and a caret. A circle is a person, as a rounded square is an
  application or a site. What it does is in [account button](#account-button).
- **Header buttons.** The collapse button, the app switcher and the language and theme button are
  icon-only buttons, so each shows its name as a tooltip on hover and on keyboard focus, see
  [buttons](actions-and-controls.md#buttons).
- **Sidebar.** The site's navigation entries, in configuration order, in a sidebar on `surface` with a
  `borderSubtle` rule on its right. An open entry shows its label alone, and a collapsed entry its icon
  alone, see [navigation icons](#navigation-icons). No entry shows a count. The entry of the current
  page is drawn in `primaryText` on a `primary` tint and marked `aria-current`.
- **A page's own sidebar.** A page with its own navigation, such as Studio, claims the sidebar by
  rendering `ShellSidebar`, and draws its navigation there in place of the site's. Such a page is a
  workspace, with `spacing-md` round its content, compact sidebar entries and a compact band, see
  [Studio workspace](studio-workspace.md#workspace). The sidebar and its collapse button show even on a
  one-page site.
- **Collapsed sidebar.** The header's button collapses the sidebar to a narrow strip of the entries'
  icons, each centred and named by a tooltip, and opens it again. Collapsed, a group's title gives way
  to a thin rule. The button names what it does, "Collapse navigation" or "Expand navigation", and has
  `aria-expanded`. The sidebar stays open or collapsed while the person moves between pages.
- **One-page sites.** A site with one navigation entry, such as the platform site, has no sidebar and
  no button to collapse it, unless a page claims the sidebar, and the page takes the whole width.
- **Narrow width.** At narrow width the sidebar gives way to a panel under the header. The header
  keeps the site title, the account button, the app switcher and a Menu button
  (`shell.navigation.menu`) with `aria-expanded`, which opens and closes the panel. The panel holds the
  navigation and the language and theme menu. Escape or following a link closes it, and Escape returns
  focus to the button. The breakpoint is in [narrow width](frontend.md#narrow-width).
- **Window.** The shell fills the browser window: the page has no margin round it, and the document
  behind it is `canvas` in both modes, so no white shows at its edges or when the page scrolls past its
  end.
- **Scrolling.** The page keeps the browser's own scroll. The header stays at the top and a compact
  band stays under it. The sidebar's background runs the page's height, while its navigation stays
  under the header with a scroll of its own. A regular band and the footer scroll with the page. The
  page declares its colour scheme, so every scrollbar, the page's and the sidebar's, follows the mode.
- **Page area.** Pages draw on `canvas` with `spacing-lg` padding. A wide table scrolls inside its
  widget, so the page never scrolls sideways.
- **Footer.** A slim footer on `surface` with a `borderSubtle` rule above it, with `spacing-xs`
  padding above and below and `spacing-lg` at its sides, and its text in `textMuted`. Without a
  sidebar it runs the full width under the page. Beside a sidebar it sits under the content column
  only, so the sidebar runs to the bottom of the window and is never pushed up by it. It holds the
  [legal line](legal.md#legal-line): the copyright "© <year> Axis" on the left and the legal links on
  the right, from the `legal` of the site the shell shows, see [legal settings](legal.md#legal-settings).
  It shows no server status, and a page that cannot reach the server says so where it fails.
- **Version.** A signed-in person also sees the server's version in the footer, after the copyright.
  An anonymous visitor never sees it, because it tells an attacker which known weaknesses to try.
- **The version as an item of its own** _(planned for M31)_. The version stands beside the copyright as
  an item of its own, "v0.1.0", never joined to it by a separator, see
  [text](actions-and-controls.md#text).

## Navigation icons

A navigation entry shows an icon when its sidebar is collapsed. The icons are Axis's own, so an
application never depends on the UI library.

- A navigation entry of a `site` resource may have an `icon`: one name from a closed list that Axis
  owns, such as `home`, `dashboard`, `list`, `inbox`, `tasks`, `form`, `document`, `folder`, `chart`,
  `process`, `people`, `person`, `calendar`, `clock`, `money`, `cart`, `tag`, `device`, `tool`,
  `support`, `mail` and `settings`. The configuration check refuses a name outside the list.
- The names are Axis's, never the UI library's. The SPA maps each name to one outlined icon, so
  replacing the library changes the map and no application.
- The map gives an entry with no icon a neutral page icon. An icon is `aria-hidden`, as the label names
  the entry.
- `GET /api/sites/{path}` gives each navigation item its `icon`, or `null`. `GET /api/site` gives the
  platform site's Home entry the `home` icon.

## Page header

Every page of a site starts with a header band on `surface` across the page area, with a
`borderSubtle` rule below it, over the page's widgets on `canvas`. It says where the person is and what
the page is.

- **Regular band.** The band holds the breadcrumb, then the page title in `heading4`. It has
  `spacing-lg` padding above and at its sides, `spacing-md` below the title, and `spacing-xs` between
  the breadcrumb and the title.
- **Breadcrumb.** The breadcrumb starts at the site and ends at the page: "Purchase requests ›
  Requests". A form page opened from a table names that table's navigation page between them,
  "Purchase requests › Requests › New request", and its link returns to the table with its paging and
  sorting. Every step but the last is a link, and the last is marked `aria-current`.
- **Compact band.** A workspace page, such as every page of Studio, draws its band as one row of the
  `bar` height: the page's state first, then its own actions. It has no breadcrumb, and it stays under
  the header while the page scrolls, see [band](studio-workspace.md#band).
- **Actions stay with their widget.** A widget's actions stay in its own toolbar, because a page can
  hold several widgets and an action belongs to its widget, not to the page.
- **Platform pages.** The platform home page has no band and no breadcrumb, and has its own headings,
  see the [platform home page](#platform-home-page). The Sessions page has the band "Axis › Your
  sessions", and the users page the band "Axis › Users".

## App switcher and preferences

Two header menus let a person move between their sites and tools, and choose the language and the
theme.

- **App switcher.** An icon button named "Switch to" opens a menu of the person's sites, by title,
  under the heading "Sites", then the tools of their roles under the heading "Tools": Studio for an
  author, the operator console for an operator and Users for an administrator, which links to the
  users page at `/users`, see [the administrator role](user-administration.md#the-administrator-role).
  An administrator in a cell also sees Billing after Users, a link to `billingUrl` that opens the
  [billing portal](billing-portal.md#where) in the same tab. Outside a cell there is no such entry.
  The entry of the current page, a site or a tool, is drawn selected, as the language and theme menu
  draws its current choices, and marked `aria-current="page"`. With nobody signed in the app switcher
  is not shown. On a site's own host it lists only that site and no tools.
- **Menus.** Every dropdown menu keeps `spacing-xs` round its items, so a selected last item does not
  sit on the menu's edge.
- **Language and theme.** An icon button with a settings gear, named "Language and theme", opens one
  menu with the site's languages and the themes, for signed-in people and for nobody signed in alike. A
  divider separates the languages from the themes. The current language and theme are drawn selected,
  in `primaryText` on a `primary` tint, not only marked for assistive technology. At narrow width the
  button sits in the Menu panel, see [layout](#layout).
- **Themes.** The themes are System, Light and Dark. System is the default when the browser holds no
  choice, and follows the operating system's light or dark setting, changing at once when it changes.
  Light and Dark keep their mode. The choice is kept in `localStorage` under `axis.themeMode`, as
  `system`, `light` or `dark`, see [browser storage](frontend.md#locale).

## Account button

The account button is the last of the header's controls on every site, see [layout](#layout).
Sign-in itself is in [sign-in and the session](security.md#sign-in-and-the-session).

- **Loading.** The account button loads `GET /api/me` each time a shell mounts, and shows nothing while
  it loads or when the load fails. The [app switcher](#app-switcher-and-preferences) reads `studio`,
  `console` and `users` from it.
- **Signed out.** With nobody signed in, it is a Sign in button, except on the home page `/`, which
  starts sign-in or shows a [card](#the-sign-in-card). It sends the browser to `/api/auth/sign-in` with
  the current path and query string as `returnUrl`, so the person comes back there, but to `/` from the
  card addresses.
- **Signed in.** With someone signed in, it shows their display name, with their avatar before it and a
  caret after it. Its menu holds Sessions, which opens the
  [account page](platform-pages.md#account-page), and Sign out, which ends the session. The server ends
  the identity provider's session itself and the browser goes to `/?signedOut`. Only when that fails
  does the browser go to the identity provider's sign-out page.
- **Texts.** The platform texts are `shell.account.signIn`, `shell.account.signOut` and
  `shell.account.label`, the menu's label.

## Platform home page

The platform home page lists a signed-in person's applications and tools, and starts sign-in for a
person with no session.

### Signed in

The page has two parts, each under a `heading5` title with no count beside it: "Applications", then
"Tools", as people use the applications most.

- **An application** is one tile across the page, in title order. A site's title is in the current
  locale, or in the site's first locale when it lacks the current one. It shows its mark, the first
  letter of its title on a `primary` tile, then its title, the `label` of its `application.json` or its
  name when it has none, and its `description` in `textMuted`, what the application is for as a whole.
  Under a `divider` rule follow all its sites, even a lone one, in title order: each a link with the
  site's mark, its title in `primaryText` and, under it in `textMuted`, the site's own `description`,
  what that site does, see [descriptions](#application-and-site-descriptions). An application with many
  sites does not push the others down, because it has the whole row to itself.
- **The tools** of the person's roles follow as tiles: Studio for an author ("Build and publish
  applications."), the operator console for an operator ("See what failed and run the commands that
  fix it.") and Users for an administrator ("Invite people and give them roles."). Each tile is its
  icon, white on a `primary` square, its title and its description, aligned to the top. The whole tile
  is the link.
- **A tile** is a panel on `surface` with a `borderSubtle` border, `radius-base` corners and
  `spacing-md` padding, its mark and its text `spacing-sm` apart. A title with no description under it
  is centred on its mark. Applications are `spacing-md` apart.
- **A tile that is a link** shows it only by its title in `primaryText`, as every link: hover changes
  nothing, and keyboard focus draws the `focusRing` outline.
- **The home grid** holds the tools, and the sites inside an application. It takes as many columns as
  it has tiles, up to three: one tile fills the row, two share it, and three or more take a third each,
  the fourth starting the next row. The grid's own CSS sets the columns, with a `spacing-md` gap, so no
  code counts the tiles. At narrow width it is one column. Every row is as tall as the tallest, and a
  description takes at most two lines, ending in an ellipsis with the whole text in a tooltip, here and
  in Studio.

### Signed out

A person who opens Axis without a session goes straight to the sign-in page of
[Axis's identity provider](identity-provider.md#theme), and Axis draws no screen of its own in front of
it. The page starts sign-in when `GET /api/me` or `GET /api/sites` answers `401`, except at the
addresses of [the sign-in card](#the-sign-in-card).

### The sign-in card

Axis shows a card only where going straight to sign-in would loop or surprise. It has the identity
provider's frame: the organization's mark and name above one card in the middle of the page, or Axis
while the organization is not known, see [theme](identity-provider.md#theme). A tenant has no name until
`tenant create` takes one, see [realms](identity-provider.md#realms). The card is on `surface` with a
`borderSubtle` border, `radius-base` corners and `spacing-xl` padding. The header shows no second Sign in
button there, and nothing starts sign-in until the person clicks the card's button. A signed-in person
who opens any of these addresses sees the launcher.

| Address             | When                                                                                                                                                             | The card shows                                                                                                              |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `/?signIn=failed`   | Every failed sign-in but declined terms                                                                                                                          | "Sign in", the warning "Sign-in did not complete. Try again." with no reason, and a full-width Sign in button               |
| `/?signIn=declined` | The person declined the terms step, see [accepting the terms](legal.md#accepting-the-terms): an `access_denied` answer whose sign-in state the server could read | The same card, with the warning "Signing in needs the terms accepted. Sign in again to accept them."                        |
| `/?signedOut`       | After sign-out: the server sends the browser there itself, once it has ended the identity provider's session                                                     | "You have signed out", "You can close this page, or sign in again." in `textMuted`, and a full-width "Sign in again" button |

Starting sign-in at once after a failure would loop, as the identity provider's session is still open.
The warning is an alert that screen readers announce, with the platform text `shell.home.signInFailed` or
`shell.home.signInDeclined`. The card's button returns to `/` after sign-in, never to any of these
addresses.

### Endpoints

The page uses three endpoints.

| Route            | What it does                                                                           |
| ---------------- | -------------------------------------------------------------------------------------- |
| `GET /api/about` | The server's informational version for a signed-in person, see [endpoints](#endpoints) |

- `GET /api/sites` lists only the sites that allow the provider of the session, so a person signed in
  through a provider that one site allows sees that site only. It gives each site:
  - its `application`, `{ "name", "titles", "descriptions" }`, so the home page can group the sites:
    the application's name, and its title and description for each locale it has texts for, from the
    `label` and `description` in its `application.json`. The title is the name when it has no label,
    and the descriptions are `null` when it has no description;
  - its own `descriptions` for each available locale, or `null`;
  - its `legal`, see [legal settings](legal.md#legal-settings).
- `GET /api/me` adds two booleans: `console`, true when the person holds the role that
  `Console:OperatorRole` names, see the [operator console](operator-console.md), and `users`, true when
  the person holds the role that `Users:AdministratorRole` names and the tenant is not a preview tenant,
  see [users settings](host-settings.md#users-settings).
- `GET /api/about` answers a signed-in person with `{ "version" }`, the server's informational version,
  the one the images carry. Without a session it answers `401`, like every other `/api` path that needs
  one.

## Application and site descriptions

The `application` manifest and each `site` may have a `description`, one line of text that the
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

- **What each says.** The application's `description` says what the application is for as a whole,
  such as "Runs the company's purchases, from the request to approval and ordering." A site's says what
  that site does and who it is for, such as "Request purchases and take them through approval.", as an
  application often has several sites for different people. The two should not repeat each other.
- **Optional texts.** Both are optional `{ "textKey" }` texts, checked like the application's `label`
  and the site's `title`: a key that no text resource holds is `AXC0028`, at `/description/textKey`.
  Without one, the home page shows the title alone.
- **The application's title.** The application's `label` is its title on the home page and in the app
  switcher, and its name when it has none, so a sample or template gives its application a `label` in
  every locale it has texts for.

`GET /api/sites` gives both descriptions for each locale, see [endpoints](#endpoints).

## Session ended

A `401` from any API call shows the sign-in prompt, a dialog titled "Your session has ended" that reads
"Sign in again to continue.", with a Sign in button. It shows when a signed-out person opens an
application page, and over the current page when a session ends during use, such as at the
re-authentication that Axis asks for every 8 hours.

- **Only the click.** The button starts the same sign-in as the [account button](#account-button), with
  the current path and query string as `returnUrl`. Only the click starts it, so a live session at the
  identity provider never signs the person in without their action.
- **It stays.** The prompt cannot be dismissed, and once shown it stays until the browser leaves the
  page. It sits at the root of the platform, above every route, so it shows even when the site itself
  fails to load with a `401`, and then nothing shows behind it, in place of the error that the site
  could not be loaded.
- **Exempt calls.** Four calls never show the prompt. `GET /api/me` answers `401` when nobody is signed
  in, so the header shows the Sign in button and no app switcher. The home page's `GET /api/sites`
  answers `401` too, and the home page then starts sign-in, see [signed out](#signed-out). The
  notification bell's calls and Studio's readiness checks stay quiet as well.
- **Texts.** The platform texts are `shell.signInPrompt.title` and `shell.signInPrompt.message`. The
  button reuses `shell.account.signIn`.

## Design system previews

The design system shows the shell, the sign-in screens and Studio in cards of their own, so a change
to the frame shows in every state. How previews work is in
[widget previews](../development.md#widget-previews).

- **The Shell group** is the seventh group of cards, after Shared states, with three previews:

  | Preview          | States                                                                                                                                                                                                                                                                                                                              |
  | ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `shell`          | Application site, Navigation collapsed, Form page, Platform site, App switcher, and Language and theme                                                                                                                                                                                                                              |
  | `signIn`         | Sign-in refused, Signed out and Session ended, with the organization's heading over the cards                                                                                                                                                                                                                                       |
  | `identitySignIn` | The sign-in page of [Axis's identity provider](identity-provider.md#theme) in Sign in, Wrong password, Organization sign-in only, Two-step verification, Forgot password, Set password, Add an authenticator, Choose how to sign in, Update your profile, Sign out, Notice, Error, and Language and theme with the gear's menu open |

- **The identity provider's pages are the theme's own.** `identitySignIn` is built from fixtures
  through the shared sign-in components in `web/src/identitySignIn/`. Its states from Two-step
  verification to Error are the same components that the theme draws.
- **More sign-in states** _(planned for M31)_. `identitySignIn` also shows Fields left empty, Terms and
  privacy, and Terms not accepted.
- **A frame holds the whole shell.** A shell preview draws the shell, from the header to the footer, in
  a frame at least as tall as its height, which grows to hold the whole shell, so several states fit
  one card.
- **The Studio group** _(planned for M31)_. The eighth group of cards, Studio, follows Shell, with the
  previews `studio` (Applications, Overview, Overview with errors and changes not live, Overview of a
  new application, Entities, Entity, Releases and Policy matrix), `studioPageDesign`,
  `studioFormDesign` and `studioProcessDesign`, drawn through the shell over fixtures of the purchase
  request sample.
