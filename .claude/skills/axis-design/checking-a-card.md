# Checking a card

A check passes only when the card behaves as it will in the artifact.

- Draw it in a frame sandboxed without `allow-forms`, about 700 px wide, with
  the card laid out at 992 px and scaled to fit, as the artifact does. A
  form's own submission is blocked there, so check that its buttons still
  work.
- Load IBM Plex Sans and put `<meta charset="utf-8">` in the page, as the
  artifact does.
- Check it in light and in dark mode, with no console error in either.
- Check a changed card on a screenshot of the whole page it draws, not the
  changed region alone. Check the card against every rule of the sections that
  [the rules for every screen](#the-rules-for-every-screen) links.
- Measure the contrast as [design tokens and previews](../../../docs/reference/frontend.md#design-tokens-and-previews)
  says: text at 4.5:1 or more and controls and borders at 3:1 or more, on `surface` and `canvas`, in both modes.
- Measure the card's content height at 992 px and set its marker's height to
  it, so nothing is cut off and no empty band is left.

## The rules for every screen

A card that draws a screen also keeps the rules for every screen. Each rule is in its reference page:

- [Text](../../../docs/reference/actions-and-controls.md#text)
- [Showing what is empty](../../../docs/reference/actions-and-controls.md#showing-what-is-empty)
- [List order](../../../docs/reference/actions-and-controls.md#list-order)
- [Opening things](../../../docs/reference/actions-and-controls.md#opening-things)
- [Layout](../../../docs/reference/shell.md#layout)
- [Buttons](../../../docs/reference/actions-and-controls.md#buttons)
- [Menus](../../../docs/reference/actions-and-controls.md#menus)
- [Fields](../../../docs/reference/actions-and-controls.md#fields)
- [Tab bars](../../../docs/reference/actions-and-controls.md#tab-bars)
- [Scroll areas](../../../docs/reference/actions-and-controls.md#scroll-areas)
- [Design tokens and previews](../../../docs/reference/frontend.md#design-tokens-and-previews), for the contrast
  in both modes
