import { createContext, type ReactNode } from 'react'

/**
 * What the Studio form canvas gives the form widget it draws. Only the canvas provides it, so the live site's
 * form widget never sees one and draws exactly as it always does.
 */
export interface FormCanvas {
  /** The index of the step to show, in place of the wizard's own. */
  step: number
  /** Wraps the section at `index` of the form, its title and its fields. */
  section: (index: number, content: ReactNode) => ReactNode
  /** Wraps the input of the field `name`. */
  field: (name: string, content: ReactNode) => ReactNode
  /**
   * Proposed, #4902: wraps the entry of step `index` in the wizard's steps bar, so the canvas's bar is the one place the
   * author picks the step shown, as the site's bar shows it; Studio draws no switch of its own above the canvas.
   */
  stepEntry: (index: number, content: ReactNode) => ReactNode
}

export const FormCanvasContext = createContext<FormCanvas | null>(null)
