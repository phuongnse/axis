import { createContext } from 'react'

export interface ShellAsideState {
  /** The element in the right column that holds a claiming page's content, or `null` while there is none. */
  target: HTMLElement | null
  /** Registers a claim on the right column, by the claimant's `id`. The returned function releases it. `closable` is whether the person opened the claimant and can close it. */
  claim: (id: string, closable: boolean) => () => void
  /** The `id` of the claim that shows, or `undefined` while no claim is registered, when every claimant shows. */
  current?: string
}

export const ShellAsideContext = createContext<ShellAsideState>({
  target: null,
  claim: () => () => {},
})
