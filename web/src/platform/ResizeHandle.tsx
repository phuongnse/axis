import { useState, type KeyboardEvent, type PointerEvent } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The handle on a panel's edge that sets its size: a `separator` that takes the arrow keys, Home and End and a drag. A
 * side panel has it on its start edge and sets its width, and a package's bottom panel has it on its top edge and sets
 * its height, see docs/reference/shell.md#layout. Proposed, see #3978: a package's explorer has it on its end edge and
 * sets its width, and the rule under a handle turns `primary` while the pointer is on it or drags it, as a code
 * editor's sash does, and a short grip in `textMuted` sits at the middle of the rule at all times, as a split view's
 * grip does, so a person sees that the edge moves before pointing at it; `labelKey` names what it resizes. Each arrow moves the edge by `spacing-md`, and the arrow that moves the
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
  labelKey = 'shell.sidePanel.resize',
}: {
  /** The panel's edge that the handle sits on. */
  edge: 'start' | 'end' | 'top'
  labelKey?: string
  size: number
  min: number
  max: number
  measure: (event: PointerEvent<HTMLDivElement>) => number
  onChange: (size: number) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [dragging, setDragging] = useState(false)
  const grow = edge === 'start' ? 'ArrowLeft' : edge === 'end' ? 'ArrowRight' : 'ArrowUp'
  const shrink = edge === 'start' ? 'ArrowRight' : edge === 'end' ? 'ArrowLeft' : 'ArrowDown'

  const onKeyDown = (event: KeyboardEvent) => {
    const target = { [grow]: size + spacing.md, [shrink]: size - spacing.md, Home: min, End: max }[event.key]
    if (target !== undefined) {
      event.preventDefault()
      onChange(target)
    }
  }
  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId)
    setDragging(true)
  }
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      onChange(measure(event))
    }
  }

  return (
    <>
      <style>{`[data-resize-handle]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: -2px; } [data-resize-handle]::after { content: ''; position: absolute; background: transparent; transition: background-color 0.1s 0.15s; } [data-resize-handle][aria-orientation='vertical']::after { inset-block: 0; inset-inline-start: calc(50% - 1px); width: 2px; } [data-resize-handle][aria-orientation='horizontal']::after { inset-inline: 0; top: calc(50% - 1px); height: 2px; } [data-resize-handle]::before { content: ''; position: absolute; z-index: 1; border-radius: 2px; background: ${colour.textMuted}; transition: background-color 0.1s 0.15s; } [data-resize-handle][aria-orientation='vertical']::before { top: calc(50% - 12px); inset-inline-start: calc(50% - 1.5px); width: 3px; height: 24px; } [data-resize-handle][aria-orientation='horizontal']::before { inset-inline-start: calc(50% - 12px); top: calc(50% - 1.5px); width: 24px; height: 3px; } [data-resize-handle]:hover::after, [data-resize-handle][data-dragging]::after, [data-resize-handle]:hover::before, [data-resize-handle][data-dragging]::before { background: ${colour.primary}; }`}</style>
      <div
        role="separator"
        aria-orientation={edge === 'top' ? 'horizontal' : 'vertical'}
        aria-label={t(labelKey)}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-valuenow={size}
        tabIndex={0}
        data-resize-handle
        data-dragging={dragging ? '' : undefined}
        onKeyDown={onKeyDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={() => setDragging(false)}
        onPointerCancel={() => setDragging(false)}
        style={{
          position: 'absolute',
          touchAction: 'none',
          zIndex: 2,
          // The handle straddles the panel's rule, so the rule it colours is the one it moves.
          ...(edge === 'start'
            ? { insetBlock: 0, insetInlineStart: -spacing.xs / 2, width: spacing.xs, cursor: 'col-resize' }
            : edge === 'end'
              ? { insetBlock: 0, insetInlineEnd: -spacing.xs / 2 - 1, width: spacing.xs, cursor: 'col-resize' }
              : { insetInline: 0, top: -spacing.xs / 2 - 1, height: spacing.xs, cursor: 'row-resize' }),
        }}
      />
    </>
  )
}
