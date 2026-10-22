import { useSyncExternalStore } from 'react'

/** Below Ant Design's `md` breakpoint, 768 px, the layout is the phone layout, see D61. */
export const phoneWidthQuery = '(max-width: 767.98px)'

function subscribe(onChange: () => void) {
  const list = window.matchMedia(phoneWidthQuery)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

function isPhoneWidth() {
  return window.matchMedia(phoneWidthQuery).matches
}

/** Whether the window is narrower than 768 px. It updates when the window is resized across it. */
export function usePhoneWidth(): boolean {
  return useSyncExternalStore(subscribe, isPhoneWidth)
}
