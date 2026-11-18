import { useContext, useLayoutEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ShellAsideContext } from './shellAsideContext'

/**
 * Claims the shell's right column for a page, see docs/reference/shell.md#layout. The column is `sidePanel` wide and
 * runs from under the header to the footer. The shell draws the children in it, and the claim ends when this
 * component unmounts.
 */
export function ShellAside({ children }: { children: ReactNode }) {
  const { target, claim } = useContext(ShellAsideContext)
  useLayoutEffect(() => claim(), [claim])
  return target ? createPortal(children, target) : null
}
