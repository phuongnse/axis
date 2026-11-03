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
  /**
   * Proposed: the texts edited but not saved, by key. With `setPending`, a label is part of the resource's one Save:
   * it holds its texts here, and the editor writes them with the resource, so a label has no Write texts of its own.
   */
  pending?: Record<string, Record<string, string>>
  setPending?: (key: string, texts: Record<string, string>) => void
} | null>(null)
