import { useState, type CSSProperties, type KeyboardEvent, type PointerEvent, type RefObject } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The thickness of the rule the handle lies over, and of each bar of its grip. */
const ruleWidth = 1
const barWidth = 2

/**
 * The one handle that sets the size of a region, see docs/reference/shell.md#layout. It is a `separator` that lies over
 * the region's rule, takes the arrow keys along its axis, Home and End, and a drag, and is named by `nameKey`. A grip of
 * two bars in `border` sits at the middle of the rule, and the rule and the grip turn `primary` under the pointer or a
 * drag. `orientation` is the rule's: a vertical rule sets the region's width and a horizontal one its height. `edge` is
 * the region's edge that the rule is on. The arrow that moves the rule away from the region grows it by `spacing-md`,
 * Home goes to `min` and End to `max`. The caller keeps the size and clamps it in `onChange`, and `region` is the element
 * the handle sizes, which a drag measures. The parent of the handle is positioned and holds the rule's edge.
 */
export function ResizeHandle({
  nameKey,
  orientation,
  edge,
  value,
  min,
  max,
  onChange,
  region,
}: {
  nameKey: string
  orientation: 'vertical' | 'horizontal'
  edge: 'start' | 'end'
  value: number
  min: number
  max: number
  onChange: (size: number) => void
  region: RefObject<HTMLElement | null>
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [hovered, setHovered] = useState(false)
  const [dragging, setDragging] = useState(false)
  const vertical = orientation === 'vertical'
  const start = edge === 'start'
  const active = hovered || dragging
  const shade = active ? colour.primary : colour.border

  const grow = vertical ? (start ? 'ArrowLeft' : 'ArrowRight') : start ? 'ArrowUp' : 'ArrowDown'
  const shrink = vertical ? (start ? 'ArrowRight' : 'ArrowLeft') : start ? 'ArrowDown' : 'ArrowUp'

  const onKeyDown = (event: KeyboardEvent) => {
    const target = { [grow]: value + spacing.md, [shrink]: value - spacing.md, Home: min, End: max }[event.key]
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
    if (!dragging || !region.current) return
    const rect = region.current.getBoundingClientRect()
    if (vertical) onChange(start ? rect.right - event.clientX : event.clientX - rect.left)
    else onChange(start ? rect.bottom - event.clientY : event.clientY - rect.top)
  }
  const endDrag = () => setDragging(false)

  // The rule lies just outside the region, so the strip is centred on it: half the strip and half the rule out.
  const outset = `calc(${-spacing.xs / 2}px - ${ruleWidth / 2}px)`
  const middle = `calc(50% - ${ruleWidth / 2}px)`
  const strip: CSSProperties = vertical
    ? {
        insetBlock: 0,
        width: spacing.xs,
        [start ? 'insetInlineStart' : 'insetInlineEnd']: outset,
        cursor: 'col-resize',
      }
    : {
        insetInline: 0,
        height: spacing.xs,
        [start ? 'insetBlockStart' : 'insetBlockEnd']: outset,
        cursor: 'row-resize',
      }
  const line: CSSProperties = vertical
    ? { insetBlock: 0, width: ruleWidth, insetInlineStart: middle }
    : { insetInline: 0, height: ruleWidth, insetBlockStart: middle }
  const bar: CSSProperties = vertical
    ? { width: barWidth, height: spacing.md, background: shade }
    : { width: spacing.md, height: barWidth, background: shade }

  return (
    <>
      <style>{`[data-resize-handle]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: -2px; }`}</style>
      <div
        role="separator"
        aria-orientation={orientation}
        aria-label={t(nameKey)}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-valuenow={value}
        tabIndex={0}
        data-resize-handle
        onKeyDown={onKeyDown}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onLostPointerCapture={endDrag}
        onPointerEnter={() => setHovered(true)}
        onPointerLeave={() => setHovered(false)}
        style={{ position: 'absolute', touchAction: 'none', ...strip }}
      >
        <span
          data-resize-line
          style={{ position: 'absolute', background: active ? colour.primary : 'transparent', ...line }}
        />
        <span
          style={{
            position: 'absolute',
            insetBlockStart: '50%',
            insetInlineStart: '50%',
            transform: 'translate(-50%, -50%)',
            display: 'flex',
            flexDirection: vertical ? 'row' : 'column',
            gap: barWidth,
          }}
        >
          <span data-resize-bar style={bar} />
          <span data-resize-bar style={bar} />
        </span>
      </div>
    </>
  )
}
