import { useSyncExternalStore } from 'react'

/** The browser setting that asks for less motion, see D97. */
export const reducedMotionQuery = '(prefers-reduced-motion: reduce)'

function subscribe(onChange: () => void) {
  const list = window.matchMedia(reducedMotionQuery)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

function prefersReducedMotion() {
  return window.matchMedia(reducedMotionQuery).matches
}

/** Whether the browser asks for reduced motion. It updates when the setting changes while the page is open. */
export function useReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, prefersReducedMotion)
}
