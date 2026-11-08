import { createContext } from 'react'
import type { LabelTextDrafts } from './draftHistory'

/**
 * The resource that a label's texts belong to: the application, the resource's file and name, and its saved content,
 * which the label counts its other users against. `texts` holds the label texts of the draft, by key, over the texts
 * the editor wrote, and `onTextsChange` sets one key's texts in the draft, so Save writes them and Undo steps through
 * them, see docs/reference/studio.md#label-texts. The application's names stay in `StudioNamesContext`.
 */
export const LabelTextsContext = createContext<{
  application: string
  file: string
  name: string | null
  saved: Record<string, unknown>
  texts: LabelTextDrafts
  onTextsChange: (pointer: string, key: string, texts: Record<string, string>) => void
} | null>(null)
