import { createContext, useContext, useMemo } from 'react'

/**
 * Set by each widget frame of the page designer's canvas, never by the site. A widget that finds it draws every action
 * as a button, also one the site would hide, and tags each button so a click on the canvas selects the action.
 * `selected` is the key of the selected action, relative to its widget, such as `rowActions/1` or `navigate`.
 */
export const CanvasActionsContext = createContext<{ selected: string | undefined } | null>(null)

/** The attributes the button of the action at `key` carries on the canvas. */
export interface CanvasActionProps {
  'data-action': string
  'aria-current': 'true' | undefined
}

/**
 * Whether the widget is drawn on the canvas, and the attributes for the button of an action there. `key` is the
 * action's place in the widget's metadata, such as `rowActions/1`, `actions/0` or `navigate`.
 */
export function useCanvasActions(): { active: boolean; props: (key: string) => CanvasActionProps | undefined } {
  const context = useContext(CanvasActionsContext)
  const selected = context?.selected
  const active = context !== null
  return useMemo(
    () => ({
      active,
      props: (key: string) =>
        active ? { 'data-action': key, 'aria-current': selected === key ? ('true' as const) : undefined } : undefined,
    }),
    [active, selected],
  )
}
