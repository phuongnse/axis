# Checking a card

A check passes only when the card behaves as it will in the artifact.

- Draw it in a frame sandboxed without `allow-forms`, about 700 px wide, with
  the card laid out at 992 px and scaled to fit, as the artifact does. A
  form's own submission is blocked there, so check that its buttons still
  work.
- Load IBM Plex Sans and put `<meta charset="utf-8">` in the page, as the
  artifact does.
- Check it in light and in dark mode, with no console error in either.
- Measure the contrast: text at 4.5:1 or more and controls and borders at 3:1
  or more, on `surface` and `canvas`, in both modes.
- Measure the card's content height at 992 px and set its marker's height to
  it, so nothing is cut off and no empty band is left.
