# D98. Rules from the shell review

Status: **Agreed**

The owner reviewed the shell, the platform home page and the sign-in screens in the Design System artifact, one
change at a time, and agreed these rules on 2026-10-28. [Shell and home page](../reference/shell.md) holds the detail.
None of it is built yet: one issue in M16 builds it, and the reference docs mark it _(planned for M16)_.

- **Layout:** a 48 px header with the site's mark and title, the notification bell, an app switcher, a language and
  theme menu and the account button; the site's navigation in a sidebar that collapses to a strip of icons, and no
  sidebar for a site with one page; a full-width footer with the copyright line, the version for a signed-in person
  only, and the server's readiness. See [layout](../reference/shell.md#layout).
- **Navigation icons:** a site's navigation entry may name an icon from a closed list of Axis names, which the SPA maps
  to the UI library's icons. This changes the rule that configuration never names an icon: it still never names the
  UI library's, as [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md) requires. See
  [navigation icons](../reference/shell.md#navigation-icons).
- **Page header:** a band with the breadcrumb above the title, from the site down, through the navigation page a form
  was opened from. A widget's actions stay in its toolbar. See [page header](../reference/shell.md#page-header).
- **App switcher and preferences:** the app switcher lists the person's sites and the tools of their roles, and
  replaces the Studio link. One menu holds the language and the theme, for everyone. See
  [app switcher and preferences](../reference/shell.md#app-switcher-and-preferences).
- **Platform home page:** from the highest level to the lowest, a welcome band with the tools of the person's roles,
  then each application as a card over its sites. With nobody signed in it is the sign-in screen. `GET /api/sites`
  adds each site's application, `GET /api/me` adds `console`, and a new `GET /api/about` gives a signed-in person the
  version. See [platform home page](../reference/shell.md#platform-home-page).
- **Session ended:** the `401` dialog says the session has ended before it asks to sign in again. See
  [session ended](../reference/shell.md#session-ended).
- **Design system previews:** a seventh group, Shell, with the `shell`, `signIn` and `identitySignIn` previews, added
  to the groups of [D96](d96-rules-from-the-design-system-review.md). See
  [design system previews](../reference/shell.md#design-system-previews).
