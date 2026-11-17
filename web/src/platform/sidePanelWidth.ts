import { useState } from 'react'
import { sizeTokens } from './designTokens'

const widthKey = 'axis.sidePanel.width'

/**
 * Proposed, see the review on #3650: the side panel's width, which the person sets by dragging its edge or with the
 * arrow keys on it, from `sidePanel` to half the shell's width. The browser keeps it for the person, so every side
 * panel opens at the width they last chose.
 */
export function useSidePanelWidth(): [number, (width: number) => void] {
  const [width, setWidth] = useState(() => {
    try {
      const kept = Number(window.localStorage.getItem(widthKey))
      return Number.isFinite(kept) && kept >= sizeTokens.sidePanel ? kept : sizeTokens.sidePanel
    } catch {
      return sizeTokens.sidePanel
    }
  })
  const keep = (next: number) => {
    setWidth(next)
    try {
      window.localStorage.setItem(widthKey, String(next))
    } catch {
      // A browser that keeps nothing opens the panel at `sidePanel` next time.
    }
  }
  return [width, keep]
}
