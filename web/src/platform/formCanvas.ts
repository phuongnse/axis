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
}

export const FormCanvasContext = createContext<FormCanvas | null>(null)
