import { createContext } from 'react'

/** The resource that a form edits. The expression editor needs it to check an expression against the saved file. */
export const ExpressionCheckContext = createContext<{ application: string; resourceId: string } | null>(null)
