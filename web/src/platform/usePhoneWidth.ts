import { createContext, useContext, useSyncExternalStore } from 'react'

/** Below Ant Design's `md` breakpoint, 768 px, the layout is the phone layout, see D61. */
export const phoneWidthQuery = '(max-width: 767.98px)'

/** Forces the phone layout on or off below a page designer's canvas. Unset, the window width decides. */
export const PhoneWidthOverride = createContext<boolean | undefined>(undefined)

function subscribe(onChange: () => void) {
  const list = window.matchMedia(phoneWidthQuery)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

function isPhoneWidth() {
  return window.matchMedia(phoneWidthQuery).matches
}

/**
 * Whether the window is narrower than 768 px. It updates when the window is resized across it. The page designer's
 * canvas overrides it through {@link PhoneWidthOverride}.
 */
export function usePhoneWidth(): boolean {
  const matches = useSyncExternalStore(subscribe, isPhoneWidth)
  return useContext(PhoneWidthOverride) ?? matches
}
