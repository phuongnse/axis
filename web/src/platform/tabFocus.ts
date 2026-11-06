let tabbed = false

document.addEventListener(
  'keydown',
  (event) => {
    tabbed = event.key === 'Tab'
  },
  true,
)
document.addEventListener(
  'keyup',
  (event) => {
    if (event.key === 'Tab') tabbed = false
  },
  true,
)
document.addEventListener(
  'pointerdown',
  () => {
    tabbed = false
  },
  true,
)
document.addEventListener(
  'mousedown',
  () => {
    tabbed = false
  },
  true,
)

/** Ant Design's focus trigger cannot tell keyboard focus from a click or a script, so this reports whether the last key was Tab. */
export function focusFollowsTab(): boolean {
  return tabbed
}
