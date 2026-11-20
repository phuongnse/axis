import { createContext } from 'react'

export interface ShellBarSlotState {
  /** The element at the foot of the shell's content column that holds a bar, or `null` while there is none. */
  target: HTMLElement | null
}

/** `null` outside a shell, where a bar draws in place. */
export const ShellBarSlotContext = createContext<ShellBarSlotState | null>(null)
