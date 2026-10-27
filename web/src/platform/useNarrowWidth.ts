import { createContext, useContext, useSyncExternalStore } from 'react'
import { breakpointTokens } from './designTokens'

/**
 * Below 992 px, Ant Design's `lg` breakpoint, the layout is narrow width, see D96. The query ends 0.02 px below the
 * token, so a window of exactly 992 px is not narrow.
 */
export const narrowWidthQuery = `(max-width: ${breakpointTokens.narrowWidth - 0.02}px)`

/** Forces the narrow layout on or off below a page designer's canvas. Unset, the window width decides. */
export const NarrowWidthOverride = createContext<boolean | undefined>(undefined)

function subscribe(onChange: () => void) {
  const list = window.matchMedia(narrowWidthQuery)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

function isNarrowWidth() {
  return window.matchMedia(narrowWidthQuery).matches
}

/**
 * Whether the window is narrower than 992 px, the one breakpoint of D96. It updates when the window is resized across
 * it. The page designer's canvas overrides it through {@link NarrowWidthOverride}.
 */
export function useNarrowWidth(): boolean {
  const matches = useSyncExternalStore(subscribe, isNarrowWidth)
  return useContext(NarrowWidthOverride) ?? matches
}
