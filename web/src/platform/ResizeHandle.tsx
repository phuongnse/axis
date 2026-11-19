import type { KeyboardEvent, PointerEvent } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The handle on a panel's edge that sets its size: a `separator` that takes the arrow keys, Home and End and a drag. A
 * side panel has it on its start edge and sets its width, and a package's bottom panel has it on its top edge and sets
 * its height, see docs/reference/shell.md#layout. Each arrow moves the edge by `spacing-md`, and the arrow that moves the
 * edge away from the panel makes it larger. `measure` gives the size that a pointer at the event's place sets, and
 * `onChange` clamps and keeps it.
 */
export function ResizeHandle({
  edge,
  size,
  min,
  max,
  measure,
  onChange,
}: {
  /** The panel's edge that the handle sits on. */
  edge: 'start' | 'top'
  size: number
  min: number
  max: number
  measure: (event: PointerEvent<HTMLDivElement>) => number
  onChange: (size: number) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const grow = edge === 'start' ? 'ArrowLeft' : 'ArrowUp'
  const shrink = edge === 'start' ? 'ArrowRight' : 'ArrowDown'

  const onKeyDown = (event: KeyboardEvent) => {
    const target = { [grow]: size + spacing.md, [shrink]: size - spacing.md, Home: min, End: max }[event.key]
    if (target !== undefined) {
      event.preventDefault()
      onChange(target)
    }
  }
  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId)
  }
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      onChange(measure(event))
    }
  }

  return (
    <>
      <style>{`[data-resize-handle]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: -2px; }`}</style>
      <div
        role="separator"
        aria-orientation={edge === 'start' ? 'vertical' : 'horizontal'}
        aria-label={t('shell.sidePanel.resize')}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-valuenow={size}
        tabIndex={0}
        data-resize-handle
        onKeyDown={onKeyDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        style={{
          position: 'absolute',
          touchAction: 'none',
          ...(edge === 'start'
            ? { insetBlock: 0, insetInlineStart: 0, width: spacing.xs, cursor: 'col-resize' }
            : { insetInline: 0, top: 0, height: spacing.xs, cursor: 'row-resize' }),
        }}
      />
    </>
  )
}
