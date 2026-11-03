# Design review 2026-11-03-studio-settings: the owner's rules

Topic: Studio's Settings page and the Off-standard tag on Releases.

The prototype starts from the tag `design-review/2026-11-01-studio-editors` (b046699d), so the owner keeps seeing D128
and D129, with `main` (b2c03324) merged in for module contracts and off-standard releases. Its own diff starts at the
merge commit e7e2a2c5. Each entry is one agreed change: what the owner asked, in English, and what was built, for the
docs change at the end of the review.

## Carried over from the studio-editors review

- **Off-standard on Releases.** On the Releases list, the Off-standard tag sits beside the release number and opens a
  popover with each relaxed point: the module's label, the point's name and the reason. The Relaxed points column goes.
  Point names show as readable labels, never technical names.

## Agreed changes
