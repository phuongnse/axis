import { useContext, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ShellBarSlotContext } from './shellBarSlotContext'

/**
 * Draws a bar in the bar slot at the foot of the shell's content column, see docs/reference/shell.md#layout. The slot
 * sticks to the bottom of the window while the page scrolls and rests above the footer at the end of the page, so a bar
 * in it stays in reach and never covers the footer. Outside a shell, as in a widget preview, the children draw in place.
 */
export function ShellBarSlot({ children }: { children: ReactNode }) {
  const slot = useContext(ShellBarSlotContext)
  if (slot === null) {
    return children
  }
  return slot.target ? createPortal(children, slot.target) : null
}
