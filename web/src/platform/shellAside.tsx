import { useContext, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { sizeTokens } from './designTokens'
import { ShellAsideContext } from './shellAsideContext'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: draws a side panel into the shell's right column, which runs from under the header to the bottom of the
 * window, as the sidebar does on the left. The column shows only while a page renders this, and a page shows one side
 * panel at a time. See the reviews on #3263 and #3650.
 */
export function ShellAside({ children }: { children: ReactNode }) {
  const { target, claim } = useContext(ShellAsideContext)
  useLayoutEffect(() => claim(), [claim])
  return target ? createPortal(children, target) : null
}

/** How far an arrow key moves the panel's edge. */
const widthStep = 16

/** The widest a side panel may be: half the shell's width, and never narrower than `sidePanel`. */
const widest = (rowWidth: number) => Math.max(sizeTokens.sidePanel, Math.floor(rowWidth / 2))

/**
 * The shell's right column and its side panel. Beside the content it lies over the row's end, as tall as the row, its
 * panel under the header to the window's bottom, with a handle on its edge that sets its width. At narrow width the
 * panel takes the window's width instead, from under the header to the window's bottom, over the page, and has no
 * handle.
 */
export function ShellAsideColumn({
  narrow,
  height,
  headerHeight,
  width,
  onWidth,
  onTarget,
}: {
  narrow: boolean
  /** The panel's height: the window's, less the header. */
  height: string
  headerHeight: number
  width: number
  onWidth: (width: number) => void
  onTarget: (element: HTMLElement | null) => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const column = useRef<HTMLDivElement>(null)
  const [dragging, setDragging] = useState(false)
  // The width of the shell's row, which sets how wide the panel may grow, measured again as the window changes.
  const [rowWidth, setRowWidth] = useState(0)
  useLayoutEffect(() => {
    const row = column.current?.parentElement
    if (!row || typeof ResizeObserver === 'undefined') {
      return
    }
    const measure = () => setRowWidth(row.getBoundingClientRect().width)
    const observer = new ResizeObserver(measure)
    observer.observe(row)
    measure()
    return () => observer.disconnect()
  }, [])
  const max = widest(rowWidth)
  const clamp = (next: number) => Math.min(max, Math.max(sizeTokens.sidePanel, next))
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const moves: Record<string, number> = { ArrowLeft: widthStep, ArrowRight: -widthStep }
    if (event.key in moves) {
      event.preventDefault()
      onWidth(clamp(width + moves[event.key]))
    } else if (event.key === 'Home') {
      event.preventDefault()
      onWidth(sizeTokens.sidePanel)
    } else if (event.key === 'End') {
      event.preventDefault()
      onWidth(max)
    }
  }

  return (
    <div
      ref={column}
      style={
        narrow
          ? {
              order: -1,
              position: 'sticky',
              top: headerHeight,
              zIndex: 9,
              height,
              marginBottom: `calc(-1 * ${height})`,
              background: colour.surface,
            }
          : {
              position: 'absolute',
              top: 0,
              bottom: 0,
              insetInlineEnd: 0,
              width,
              background: colour.surface,
              borderInlineStart: `1px solid ${colour.borderSubtle}`,
            }
      }
    >
      <div
        style={{
          position: narrow ? undefined : 'sticky',
          top: narrow ? undefined : headerHeight,
          height: narrow ? '100%' : height,
          display: 'flex',
        }}
      >
        {!narrow && (
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label={t('shell.sidePanel.resize')}
            aria-valuemin={sizeTokens.sidePanel}
            aria-valuemax={max}
            aria-valuenow={width}
            tabIndex={0}
            data-testid="side-panel-resize"
            onKeyDown={onKeyDown}
            onPointerDown={(event) => {
              event.preventDefault()
              event.currentTarget.setPointerCapture(event.pointerId)
              setDragging(true)
            }}
            onPointerMove={(event) => {
              const row = column.current?.parentElement
              if (dragging && row) {
                onWidth(clamp(row.getBoundingClientRect().right - event.clientX))
              }
            }}
            onPointerUp={() => setDragging(false)}
            onPointerCancel={() => setDragging(false)}
            style={{
              position: 'absolute',
              insetInlineStart: -3,
              top: 0,
              bottom: 0,
              width: 6,
              zIndex: 1,
              cursor: 'col-resize',
              touchAction: 'none',
              background: dragging ? colour.primary : 'transparent',
              opacity: dragging ? 0.4 : 1,
            }}
          />
        )}
        <div ref={onTarget} style={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minWidth: 0 }} />
      </div>
    </div>
  )
}
