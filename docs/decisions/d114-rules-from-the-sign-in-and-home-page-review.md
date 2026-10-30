# D114. Rules from the sign-in and home page review

Status: **Agreed**

The owner reviewed the sign-in screens, the shell's menus and the platform home page in the Design System artifact,
one change at a time, and agreed these rules on 2026-10-30. They change some rules of
[D98](d98-rules-from-the-shell-review.md): the sign-in screen in front of every sign-in, the welcome band and the
home page's cards. [Shell and home page](../reference/shell.md) and
[Axis's identity provider](../reference/identity-provider.md#theme) hold the detail. None of it is built yet.

- **The organization heads the sign-in pages.** Every page of the identity provider, and Axis's own sign-in card,
  shows the organization's mark and name above one card, in place of the Axis mark. The card's title names the step
  and does not repeat the name. While the organization is not known, the heading names Axis. See
  [theme](../reference/identity-provider.md#theme) and [platform home page](../reference/shell.md#platform-home-page).
- **Sign-in starts at once.** A person who opens Axis without a session goes straight to the identity provider's
  sign-in page. This replaces D98's sign-in screen, where nothing started until the person clicked. Axis shows a card
  only where going straight to sign-in would loop or surprise: after a refused sign-in, and after sign-out, which now
  returns to `/?signedOut`. See [platform home page](../reference/shell.md#platform-home-page).
- **The shell fills the window.** The page has no margin round the shell, and behind it is `canvas` in both modes.
  See [layout](../reference/shell.md#layout).
- **Menus mark the current entry.** The app switcher marks the site or tool of the current page, as the language and
  theme menu marks its choices, and every dropdown menu keeps `spacing-xs` round its items. See
  [app switcher and preferences](../reference/shell.md#app-switcher-and-preferences).
- **The theme can follow the system.** The theme menu offers System, Light and Dark. System is the default and follows
  the operating system's setting as it changes. See
  [app switcher and preferences](../reference/shell.md#app-switcher-and-preferences).
- **The home page shows applications, then tools.** The welcome band goes. Each application takes a whole row, with
  its mark, title and description over its sites; each site shows its mark, title and description. The tools follow
  as tiles. The tiles share a grid that takes as many columns as it has tiles, up to three. A tile that is a link
  shows it by its title in `primaryText` only. No heading shows a count. See
  [platform home page](../reference/shell.md#platform-home-page).
- **Applications and sites have descriptions.** An `application` and a `site` may each declare an optional
  `description` text, the application's what it is for as a whole and the site's what that site does, and
  `GET /api/sites` gives both per locale. This is new configuration. See
  [descriptions](../reference/shell.md#application-and-site-descriptions).
