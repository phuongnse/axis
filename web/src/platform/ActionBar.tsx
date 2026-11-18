import { Flex } from 'antd'
import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AxisActionRow, type AxisFoldAction } from './AxisButton'
import { useDesignTokens } from './themeMode'

/**
 * A bar of actions at the foot of a page, see docs/reference/actions-and-controls.md#buttons. It is drawn on `surface`
 * with a `borderSubtle` rule above it. `start` is drawn at its start edge. The `fold` actions and `end` are drawn at its
 * end edge, and the `fold` actions become the entries of one More actions menu when the bar is too narrow, as
 * `AxisActionRow` folds them. The bar has no margin and no position, so its caller places it, unless `docked` is
 * given: then the bar stays at the foot of the window across the content, which is the nearest main, so it leaves the
 * sidebar uncovered, and it leaves its own height in the page so that the last content is not hidden. `data-testid`
 * names that fixed wrapper.
 */
export function ActionBar({
  start,
  fold,
  end,
  docked = false,
  'data-testid': testId,
}: {
  start?: ReactNode
  fold?: AxisFoldAction[]
  end?: ReactNode
  docked?: boolean
  'data-testid'?: string
}): ReactNode {
  const { size, colour, spacing } = useDesignTokens()
  const anchorRef = useRef<HTMLDivElement>(null)
  const [frame, setFrame] = useState<{ left: number; width: number }>()

  useLayoutEffect(() => {
    const main = anchorRef.current?.closest('main')
    if (!docked || !main) {
      return
    }
    const measure = () => {
      const { left, width } = main.getBoundingClientRect()
      setFrame((current) => (current?.left === left && current.width === width ? current : { left, width }))
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(main)
    window.addEventListener('resize', measure)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [docked])

  const bar = (
    <Flex
      data-action-bar
      align="center"
      justify="space-between"
      gap="middle"
      style={{
        minHeight: size.bar,
        boxSizing: 'border-box',
        paddingBlock: spacing.xs,
        paddingInline: spacing.lg,
        background: colour.surface,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      {start !== undefined && <AxisActionRow align="center">{start}</AxisActionRow>}
      <AxisActionRow align="center" justify="flex-end" fold={fold ?? []} end={end} />
    </Flex>
  )
  if (!docked) {
    return bar
  }
  return (
    <>
      <div ref={anchorRef} style={{ height: size.bar }} />
      <div
        data-testid={testId}
        style={{ position: 'fixed', bottom: 0, left: frame?.left ?? 0, width: frame?.width ?? '100%', zIndex: 10 }}
      >
        {bar}
      </div>
    </>
  )
}
