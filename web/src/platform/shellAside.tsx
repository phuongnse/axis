import { useContext, useId, useLayoutEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ContentInsetContext } from './contentInsetContext'
import { ShellAsideContext } from './shellAsideContext'

/**
 * Claims the shell's right column for a page, see docs/reference/shell.md#layout. The column is as wide as the panel in it
 * and runs from under the header to the footer. The shell draws the children in it, and the claim ends when this
 * component unmounts. One claim shows at a time: the newest one that is `closable`, which a person opened and can close,
 * or, when none is, the newest. A claim that does not show draws nothing and comes back when the claim that hid it ends.
 * The column is outside the content area's padding, so what it holds, a side panel's included, reads an inset of 0.
 * At narrow width the column stacks under the content, see docs/reference/shell.md#layout.
 */
export function ShellAside({ closable = true, children }: { closable?: boolean; children: ReactNode }) {
  const { target, claim, current } = useContext(ShellAsideContext)
  const id = useId()
  useLayoutEffect(() => claim(id, closable), [claim, id, closable])
  return target && (current === undefined || current === id)
    ? createPortal(<ContentInsetContext.Provider value={0}>{children}</ContentInsetContext.Provider>, target)
    : null
}
