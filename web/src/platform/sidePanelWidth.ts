import { useCallback, useSyncExternalStore } from 'react'
import { sizeTokens } from './designTokens'

const storageKey = 'axis.sidePanelWidth'

const listeners = new Set<() => void>()

/** The width set while storage is unavailable (private mode), so a panel still follows its handle for the session. */
let unstored: string | null = null

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  window.addEventListener('storage', listener)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', listener)
  }
}

function stored(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return unstored
  }
}

/** The least width of a side panel and the most, which is half the window, as the shell fills the window. */
function limits(): { min: number; max: number } {
  const min = sizeTokens.sidePanel
  return { min, max: Math.max(min, Math.floor(window.innerWidth / 2)) }
}

function clamp(width: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, width))
}

/**
 * The width of every side panel, see docs/reference/shell.md#layout: it starts at `sidePanel`, the least width, a person
 * sets it up to half the window and the browser keeps it in `localStorage`, as it keeps the theme and the locale, so every
 * side panel opens at the width last chosen. A missing or invalid stored width is the least width. `set` clamps and stores
 * a width, and panels that are mounted follow it.
 */
export function useSidePanelWidth(): { width: number; min: number; max: number; set: (width: number) => void } {
  const raw = useSyncExternalStore(subscribe, stored)
  const { min, max } = limits()
  const parsed = raw === null ? Number.NaN : Number(raw)
  const width = Number.isFinite(parsed) ? clamp(parsed, min, max) : min
  const set = useCallback((next: number) => {
    const { min: least, max: most } = limits()
    const value = String(clamp(Math.round(next), least, most))
    try {
      localStorage.setItem(storageKey, value)
    } catch {
      unstored = value
    }
    listeners.forEach((listener) => listener())
  }, [])
  return { width, min, max, set }
}
