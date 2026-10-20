import { createContext } from 'react'
import type { StudioDiagnostic } from '../platform/studio'

/**
 * The resource that a label's texts belong to: the application, the resource's file and name, and its saved content,
 * which the label counts its other users against. `onTextsWritten` runs after a text write, with the folder's
 * diagnostics. The application's names stay in `StudioNamesContext`.
 */
export const LabelTextsContext = createContext<{
  application: string
  file: string
  name: string | null
  saved: Record<string, unknown>
  onTextsWritten: (diagnostics: StudioDiagnostic[]) => void
} | null>(null)
