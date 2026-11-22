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
- **The bell's audience** _(planned for M35)_. Every signed-in person on the platform site has the bell, see [the bell](notifications.md#the-bell).
- **Read-only banner.** While the tenant is [read-only](subscriptions.md#the-read-only-tenant), a
  warning banner with the alert role sits at the top of the content, under the header and beside the
  sidebar, on every page of every site. It says that the workspace is read-only and why. An
  administrator in a cell also gets a link to the billing page, the `billingUrl` of
  `GET /api/me`, see [the current user](security.md#roles-and-the-current-user). The cell console's host has no tenant, so it shows no banner.
- **Mark and title.** The mark is the first letter of the site's title on a `primary` tile with
  `radius-base` corners, and the mark and the title are one link to the site's first navigation page.
  The platform site's title and mark name Axis, because the tenant has no name yet, as the
  [sign-in card](#the-sign-in-card) does.
- **Home page for each role** _(planned for M42)_. The mark and the title link to the person's home page, not the first
  navigation page. See [sites and pages](resource-files.md#sites-and-pages) and [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).
- **Logo and colour** _(planned for M42)_. A site with a `logo` shows it in place of the mark. Its main colour takes the
  place of `primary` in its shell and pages, see [design tokens](frontend.md#design-tokens-and-previews).
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
- **Where a page is opened from**. The entry whose path is the page's path or a parent of it is marked. A page that
  matches no entry, such as a form at `/<site>/request/new` opened from a table, marks the entry of the address it was
  opened from, its `from`, which a table or a record link passes in history state. A page opened with no `from`, such as
  in a new tab, marks no entry.
- **Parent entries** _(planned for M35)_. A `ShellNavigation` entry that holds entries is a parent entry, drawn as Ant
  Design's inline submenu.
- **Menu groups** _(planned for M42)_. A site's navigation entry with `entries` is a parent entry, as
  [sites and pages](resource-files.md#sites-and-pages) sets out. It stays open while it holds the current page, and it
  shows in the narrow-width menu too. A parent left with no entry by policies is left out, see
  [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).
- **A page's own sidebar.** A page with its own navigation, such as Studio, claims the sidebar by
  rendering `ShellSidebar`, and draws its navigation there in place of the site's. Such a page is a
  workspace, with `spacing-md` round its content, compact sidebar entries and a compact band, see
  [Studio workspace](studio-workspace.md#workspace). The sidebar and its collapse button show even on a
  one-page site.
- **One side panel frame.** Every page draws its side panel in `SidePanel`, in a column on the right
  from under the header to the footer. One shows at a time, so opening one, such as the problems panel or the Ask AI
  request panel, closes the other. It holds details or tools for what is chosen, while the page stays in use. A part of
  what the page edits opens in the panel, and a short task that stands alone is a dialog, as
  [opening things](actions-and-controls.md#opening-things) says. A handle on its edge, a `separator` that takes the
  arrow keys, Home and End, sets its width from `sidePanel` to half the shell, and the browser keeps that width, so
  every side panel opens at the width last chosen. Its title can carry a `status`, such as a `StatusTag`, and a line under it. Its head
  stays in place and its body scrolls on its own, keeping room for its scrollbar, so its content keeps its width when
  a scrollbar comes. A panel with no close button, such as the process designer's step panel, gives way while a panel
  the person opened is open, and comes back when that one closes. The page keeps the browser's scroll.
- **A side panel's scroll** _(planned)_. The panel is as tall as the column under the header and scrolls from its top,
  with its scrollbar at its edge. Its head, the name and the close button, stays at the top of that scroll on
  `surface`, as the band stays at the top of the page's frame. A `borderSubtle` rule runs under the head, from the
  panel's start edge to its scrollbar, so the body that scrolls under it is parted from it.
- **A side panel's head names what it shows** _(planned)_. The head names what the panel shows, such as the field's
  name or New field, though the field's Name box repeats it. Once the body scrolls, the head tells which item is open,
  and the panel's name is its accessible name.
- **A side panel's actions** _(planned)_. `SidePanel` takes `actions`, drawn in an `ActionBar` at the panel's foot that
  stays in place under the body. The actions are outlined, with the main action last on the right, so Cancel stands
  before the main action. Its actions end where the body's content ends, past the room the body keeps for its scrollbar.
- **A part of the page in the side panel** _(planned)_. A part of what the page edits, such as an entity's field while
  the other fields show, is edited in the side panel rather than a dialog. The panel keeps what is typed until Done, or
  the main action of a new part, and Cancel stands beside that action in its bar. Cancel and Close drop what is typed, and the panel asks first when
  that would drop text.
- **A side panel's parts** _(planned)_. A side panel's parts are sections, each with its heading and a line under it. A
  `borderSubtle` rule runs between them from the panel's edge to its scrollbar.
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
  end. Text that draws no colour of its own takes `text` of the mode, as the controls' text does.
- **Scrolling.** The page keeps the browser's own scroll. The header stays at the top and a compact
  band stays under it. The sidebar's background runs the page's height, while its navigation stays
  under the header with a scroll of its own. That scroll stops at its end, as
  [scroll areas](actions-and-controls.md#scroll-areas) say. A regular band and the footer scroll with the
  page. The page declares its colour scheme, so every scrollbar, the page's and the sidebar's, follows the mode.
- **Each region scrolls on its own** _(planned)_. The window does not scroll, and the shell is as tall as the
  window. The header stays at the top. Under it each region scrolls in a frame of its own, with its scrollbar at
  its own edge: the sidebar's menu, the page's frame and the side panel. So a scrollbar always stands beside what
  it scrolls, as in a workspace such as VS Code or Gmail. Each region stops at its end, see
  [scroll areas](actions-and-controls.md#scroll-areas).
- **What stays at the top of the page** _(planned)_. What stays at the top of the page, such as Studio's compact
  band, stays at the top of the page's frame.
- **Flush page area.** Pages draw on `surface`, flush with the frame, with `spacing-lg` padding. A wide table
  scrolls inside its widget, so the page never scrolls sideways.
- **Flush page area for sites.** An application site's pages draw on the same flush page area, with no
  `canvas` under or between the header, the sidebar and the page. A list page's table sits straight under the heading.
  A small block that stands for one thing, such as a home page tile or a dashboard's KPI or chart, keeps its own
  `borderSubtle` border and `radius-base` corners.
- **Parts a rule apart.** A page's parts sit a `borderSubtle` rule apart, and each part's heading is an `h2`
  drawn at `heading5`. Each part draws the rule above itself, so a part that shows nothing leaves no rule. On a page the
  rule reaches past the page's padding to the page area's edges. In a drawer, a dialog, the sidebar, a
  tile or a column of a grid it stays inside. A runtime page's widget list, a record's detail and a record form also
  keep it inside.
- **Rules, not boxes** _(planned)_. One `borderSubtle` rule parts a block from the one beside it, and no border, box
  or rounded corner goes round a region. It follows dividers over containers, as Material's containment has it, and a
  code editor's split panes, and keeps clear of boxes inside boxes.
- **Where a rule goes** _(planned)_. A rule goes under a block's title row, as under Files, Input, Output and Log, and
  where one block ends and the next begins, such as between a run's Output and its Log. Inside a block's content no
  rule is drawn at will: the parts of one task sit together under one title, set apart by spacing. Where a rule is
  needed is judged on the whole page, not by applying a rule everywhere.
- **A rule runs the whole way**. It runs the length of what it parts, from one rule to the next or to the
  edge, as the rules under the header and beside the sidebar do.
- **What fills a region fills it to its rules** _(planned)_. A log, a code editor or a table reaches the region's
  rules, with no box of its own.
- **One inset per region** _(planned)_. A region's title row, its content and its action bar start and end at the same
  inset, `spacing-md` in a pane of a workspace and `spacing-lg` on a page. An action bar at the foot of a pane takes the
  pane's inset and keeps its actions' outlines, as it holds the pane's main action, and its look never depends on
  which buttons a person's rights show.
- **Fields as wide as each other** _(planned)_. Every field of a region is as wide as the others, and no button stands
  loose beside an input. An action of the whole region, such as Reset or Import, sits in its title row, and an action
  of one control goes with the control.
- **A title row's tools are light** _(planned)_. Tools such as the explorer's New and Refresh are subtle buttons, or
  icon-only ones with their names as tooltips, at the usual button size. A region's main action there keeps its border,
  as [buttons](actions-and-controls.md#buttons) says.
- **A title row's actions keep their roles.** `TitleRowActions` draws the actions of a part's and a field group's title
  row, each in its own role: a section's Add is a secondary button with its border, and a tool is subtle.
- **A view switch sits above what it switches** _(planned)_. A switch between two views of one content, such as Form
  and JSON, is a row of line tabs right above that content, in the one look every tab row of the workspace has, see
  [tab bars](actions-and-controls.md#tab-bars).
- **The layout follows the work**. A region puts what to act on first, then the input, the action and the
  result. An action sits after what it acts on, at the foot of its region when the input can be long, and the result
  comes after it.
- **A handle on a rule** _(planned)_. Every size handle, the side panel's included, is one handle: a grip of two bars
  in `border` at the middle of its rule, lying over the rule, and the rule and the grip turn `primary` under the
  pointer or a drag. `ResizeHandle` draws it.
- **Action bar** _(planned for M35)_. A page's action bar sits at the foot of the page area.
- **The bar slot.** The save bar and a Studio request's guide draw in the shell's bar slot, `ShellBarSlot`, in the
  content column right above the footer. The slot sticks to the window's foot while the page scrolls and rests above
  the footer at the page's end, so a bar never covers the footer. Outside a shell, as in a widget preview, a bar draws
  in place. See the [save bar](studio-resource-form.md#save-bar).
- **The page's frame holds the save bar** _(planned)_. The page's frame holds the page and the bar slot at its foot,
  so the save bar stays at the frame's foot while the page scrolls.
- **No link back** _(planned)_. A page has no link back, because the navigation and the browser lead back. A record's
  page whose record does not exist keeps one link to its list, see [routing](frontend.md#routing).
- **Footer.** A slim footer on `surface` with a `borderSubtle` rule above it, with `spacing-xs`
  padding above and below and `spacing-lg` at its sides, and its text in `textMuted`. Without a
  sidebar it runs the full width under the page. Beside a sidebar it sits under the content column
  only, so the sidebar runs to the bottom of the window and is never pushed up by it. It holds the
  [legal line](legal.md#legal-line): the copyright "© <year> Axis" on the left and the legal links on
  the right, from the `legal` of the site the shell shows, see [legal settings](legal.md#legal-settings).
  It shows no server status, and a page that cannot reach the server says so where it fails.
- **No footer** _(planned)_. The shell has no footer, so no row of the window is kept for one, and it shows no copyright
  and no version. App shells such as Gmail, Slack or Jira show no footer, and a footer at the end of the page's scroll
  would span only the page's column. The legal links are in the [account button](#account-button) for a signed-in
  person, see the [legal line](legal.md#legal-line).
- **Version.** A signed-in person also sees the server's version in the footer, after the copyright.
  An anonymous visitor never sees it, because it tells an attacker which known weaknesses to try.
- **The version as an item of its own**. The version stands beside the copyright as
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

A page with a band starts with a header band on `surface` across the page area, with a `borderSubtle` rule
below it. It says what the page is.

- **Title alone in the band.** The band of a platform page that keeps one holds only the page title in
  `heading4`, as the page's level-one heading, such as "Users" or "Your account": the account, users, user, licences,
  console and console instance pages. It has `spacing-md` padding above and below the title, so the title sits evenly,
  and `spacing-lg` at its sides, where the title lines up with the content below. `PageLayout`'s `band` switches it on.
- **No breadcrumb.** No page of any site draws a breadcrumb. The header's mark leads to the site's first
  page, and the browser goes back, as **No link back** in [layout](#layout) says. The user page and the console instance
  page have no step back to their list, because the browser leads back to it. `PageLayout` takes no breadcrumb, and no
  text names one.
- **Site pages**. A page of an application site starts with its title in `heading4` as the page's
  level-one heading, with no breadcrumb and no band of its own. The sidebar marks where the person is, and the browser
  goes back, with a table's search, sort and page kept in the address.
- **Compact band.** A workspace page, such as every page of Studio, draws its band as one row of the
  `bar` height: the page's state first, then its own actions. It has no breadcrumb, and it stays under
  the header while the page scrolls, see [band](studio-workspace.md#band).
- **Actions stay with their widget.** A widget's actions stay in its own toolbar, because a page can
  hold several widgets and an action belongs to its widget, not to the page.
- **A record's actions.** A form's actions on its record are the page's own and sit on the right of the
  page's heading, on its row, see [start process actions](form-widget.md#start-process-actions). A task page's
  Release, Claim and Reassign… and its outcomes sit there too, see [task page](task-inbox-widget.md#task-page). Other
  widgets' actions stay with them.
- **A page that shows one thing** _(planned)_. The heading is the thing's name. The state the platform keeps of the
  thing is a tag beside the name, such as a task's Overdue, a process instance's Running or a release's Live. The line
  under the heading, in `textMuted`, is the thing's kind alone, such as Request or Task, and nothing more. A record has
  no tag, as its status is a field the author defines and its form shows. A new record has no name yet, so it is headed
  "New {entity}" ("{entity} mới", `shell.form.newRecord`), with the entity's label keeping its capitals, and it has no
  line. This shape holds for every page of the shell that shows one thing: a saved or new record, a task, a process
  instance, a Studio resource, release or revision, the user page and a cell's tenant page. One `PageHeading` draws
  every one of them, filling `PageLayout`'s name, tags and line, see [which widget](shared-widgets.md#which-widget).
- **Facts as the first part** _(planned)_. What else the platform keeps of the thing, and the page does not show
  elsewhere, is the page's first part. It is a list of facts through `TermList`, see
  [fields](actions-and-controls.md#fields), above a `borderSubtle` rule. It never sits on the heading's line.
- **Platform pages.** The platform home page has no band and no breadcrumb, and has its own headings,
  see the [platform home page](#platform-home-page).

## App switcher and preferences

Two header menus let a person move between their sites and tools, and choose the language and the
theme.

- **App switcher.** An icon button named "Switch to" opens a menu of the person's sites, by title,
  under the heading "Sites", then the tools of their roles under the heading "Tools": Studio for an
  author, the operator console for an operator and Users for an administrator, which links to the
  users page at `/users`, see [the administrator role](user-administration.md#the-administrator-role).
  An administrator in a cell also sees Billing after Users, a link to `billingUrl`, the billing page, that opens in the same tab, see
  [the current user](security.md#roles-and-the-current-user). Outside a cell there is no such entry.
  The entry of the current page, a site or a tool, is drawn selected, as the language and theme menu
  draws its current choices, and marked `aria-current="page"`. With nobody signed in the app switcher
  is not shown. On a site's own host it lists only that site and no tools.
- **Menus.** Every dropdown menu keeps `spacing-xs` round its items, so a selected last item does not
  sit on the menu's edge.
- **Inset as Ant Design draws it** _(planned for M35)_. Menu items keep the inset that Ant Design draws, with no spacing
  of Axis's own round them.
- **Language and theme.** An icon button with a settings gear, named "Language and theme", opens one
  menu with the site's languages and the themes, for signed-in people and for nobody signed in alike. A
  divider separates the languages from the themes. The current language and theme are drawn selected,
  [as a marked choice](actions-and-controls.md#menus) is, not only marked for assistive technology. At
  narrow width the button sits in the Menu panel, see [layout](#layout).
- **No divider between the groups** _(planned for M35)_. No divider separates the languages from the themes.
- **Themes.** The themes are System, Light and Dark. System is the default when the browser holds no
  choice, and follows the operating system's light or dark setting, changing at once when it changes.
  Light and Dark keep their mode. The choice is kept in `localStorage` under `axis.themeMode`, as
  `system`, `light` or `dark`, see [browser storage](frontend.md#locale).

## Favourites and recent items

_(planned for M42)_

Each person keeps favourites and recent items on the server, for each application site, as [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md) sets out. The routes are in [site endpoints](frontend.md#site-endpoints).

- **Favourites.** A favourite is a page of the site, with the record that the page shows, if it shows one. The page
  header's toggle adds the current page or removes it.
- **Recent items.** A recent item is a page that showed one record: a form page at `/{id}`, or a page with a `detail`
  widget. Opening one notes it, and the list keeps the latest 20.
- **The header menu.** A menu in the header lists both kinds by page title and record label.
- **Entries left out.** An entry the person can no longer open is left out of the menu.
- **Application sites only.** The platform site has no lists. On a site's own host, only that site's lists show.
- **Look.** The look is left to [design changes](../delivery.md#design-changes).

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
  caret after it. Sign out in its menu ends the session. The server ends
  the identity provider's session itself and the browser goes to `/?signedOut`. Only when that fails
  does the browser go to the identity provider's sign-out page.
- **Account menu.** Its menu holds Your account (`shell.account.title`), which opens the
  [account page](platform-pages.md#tabs), and Sign out, as [signed in](#account-button) says.
- **The Legal group** _(planned)_. Between Your account and Sign out, each part set off by a divider, the menu holds a
  group named Legal, `shell.legal.label`. It holds Privacy and Terms when the site or its tenant sets them, then
  Licences, always, as the language and theme menu groups its choices. Its links and labels are the
  [legal line](legal.md#legal-line)'s.
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
- **Every tile is a `Tile`.** The home page's applications and tools, Studio's application tiles, and a
  dashboard's KPI and chart are all drawn through one shared tile, so they keep one look.
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
`tenant create` takes one, see [realms](identity-provider.md#realms). The [card page](shared-widgets.md#which-widget)
draws the card, as wide as the `small` dialog size token. It leaves out the language menu and the legal line, because
the shell's header and footer have them. The card is on `surface` with a
`borderSubtle` border, `radius-base` corners and `spacing-xl` padding. The header shows no second Sign in
button there, and nothing starts sign-in until the person clicks the card's button. A signed-in person
who opens any of these addresses sees the launcher.

| Address             | When                                                                                                                                                             | The card shows                                                                                                              |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `/?signIn=failed`   | Every failed sign-in but declined terms                                                                                                                          | "Sign in", the warning "Sign-in did not finish. Try again." with no reason, and a full-width Sign in button                 |
| `/?signIn=declined` | The person declined the terms step, see [accepting the terms](legal.md#accepting-the-terms): an `access_denied` answer whose sign-in state the server could read | The same card, with the warning "You need to accept the terms to sign in. Sign in again to accept them."                    |
| `/?signedOut`       | After sign-out: the server sends the browser there itself, once it has ended the identity provider's session                                                     | "You have signed out", "You can close this page, or sign in again." in `textMuted`, and a full-width "Sign in again" button |

Starting sign-in at once after a failure would loop, as the identity provider's session is still open.
The warning is an alert that screen readers announce, with the platform text `shell.home.signInFailed` or
`shell.home.signInDeclined`. The card's button returns to `/` after sign-in, never to any of these
addresses.
- **The legal line under the card** _(planned)_. The card ends in the legal line, under it and as wide as it, as the
  identity provider's card does. The shell has no footer to hold the line.

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
[widget previews](widget-previews.md).

- **The Shell group** is the seventh group of cards, after Shared states, with three previews:

  | Preview          | States                                                                                                                                                                                                                                                                                                                                                                                        |
  | ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `shell`          | Application site, Navigation collapsed, Form page, Default record page, Saved record, Saved record with a change, Record not found, Task, Task not found, Dashboard, Process instance, Platform site, App switcher, Language and theme, Account page, Account notifications, Users page, and User page                                                                                        |
  | `signIn`         | Sign-in refused, Signed out and Session ended, with the organization's heading over the cards                                                                                                                                                                                                                                                                                                 |
  | `identitySignIn` | The sign-in page of [Axis's identity provider](identity-provider.md#theme) in Sign in, Wrong password, Fields left empty, Organization sign-in only, Two-step verification, Forgot password, Set password, Add an authenticator, Choose how to sign in, Update your profile, Terms and privacy, Terms not accepted, Sign out, Notice, Error, and Language and theme with the gear's menu open |

- **The identity provider's pages are the theme's own.** `identitySignIn` is built from fixtures
  through the shared sign-in components in `web/src/identitySignIn/`. Its states from Two-step
  verification to Error are the same components that the theme draws.
- **A frame is a screen.** A shell preview draws the shell in a frame that fills the window down to the canvas's
  padding and is never shorter than the state's height. The page scrolls inside it as in a window, so a dialog
  opens in the middle of what shows.
- **The State control.** `shell`, `signIn` and `identitySignIn` show one state at a time under a State
  control, as `studio` does, and `&state=<slug>` opens one.
- **Account states.** The `shell` preview gains Account page, the platform's `/account` on its
  Security tab, and Account notifications, the same page on its Notifications tab. Both use fixtures of
  `GET /api/me/authenticators`, `GET /api/me/sessions` and `GET /api/me/notification-preferences`, and `PUT`
  answers `204`.
- **Users states.** The `shell` preview gains Users page, the platform's `/users`, and User page, `/users/{id}` of the
  first person listed. Both use fixtures of `GET /api/users`, `GET /api/users/{id}/roles` and
  `GET /api/me/tables/users`. The five tenant sections of the users page, `GET /api/tenant/outside-clients`, `usage`,
  `hosts`, `model-key` and `application-packages`, answer `404`, so the page shows only its people.
- **The preview draws the platform's own routes.** `ShellPreview` renders `PlatformRoutes`, the route table that the app
  draws, so the preview's platform pages cannot drift from the real ones.
- **Process instance.** The `shell` preview gains Process instance, which opens a waiting instance from the fixture
  site's Approvals page, on its Diagram. It uses fixtures of the instance detail and of the instance's release texts.
- **The Studio group** is the eighth group of cards, after Shell. Its `studio` preview draws Studio
  through the shell over fixtures of the purchase request sample, in sixteen states: Typical, Empty,
  Loading, Failed, Many rows, Long names, Access denied, Overview with errors and changes not live,
  Overview of a new application, Entity, Entity with expressions, Policy matrix, Releases, History,
  Revision and Release page. Clicks in the sidebar move between Overview, Settings, the kind lists and
  Releases, all answered from fixtures. Entity opens an entity in the entity designer and Policy matrix
  the policy matrix, both over the fixtures of Typical. Entity with expressions opens an entity whose
  computed field and validation read their expressions back in words, over the fixtures of Typical.
  Releases opens the release list and History the revision list, over the same fixtures. Revision opens
  one revision's page, with its three changed files, over the fixtures of Typical. Release page opens
  release 2 over the fixtures of Typical.
- **Studio's designers** are three previews that follow `studio`: `studioPageDesign`, `studioFormDesign`
  and `studioProcessDesign`. Each draws Studio through the shell with a page, a form or a process open in
  its designer. Each has one state, Typical, over the fixtures of the `studio` preview's Typical state.
