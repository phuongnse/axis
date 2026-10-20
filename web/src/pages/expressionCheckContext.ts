import { createContext } from 'react'
import type { StudioNames } from '../platform/studio'

/**
 * The resource that a form edits. The expression editor checks an expression against the saved file. It offers the
 * names in scope from the resource's kind and the unsaved draft, and the application's names from `names`, which is
 * null while they are not loaded.
 */
export const ExpressionCheckContext = createContext<{
  application: string
  resourceId: string
  kind: string
  draft: Record<string, unknown>
  names: StudioNames | null
} | null>(null)
