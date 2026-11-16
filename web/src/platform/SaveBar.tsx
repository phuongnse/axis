import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Flex, Modal } from 'antd'
import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { AxisActionRow, AxisButton } from './AxisButton'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useUnsavedChanges } from './unsavedChanges'

/**
 * Proposed, see the review on #3650: a site's record form draws its save in this bar too, with no Undo and Redo.
 * The save bar of a Studio resource, see docs/reference/studio-resource-form.md#save-bar. It stays at the foot of the
 * window across the content, over the footer, while there is something to save or a step can be redone, and it is not
 * drawn otherwise. Discard asks first, because no Undo brings the changes back, see
 * docs/reference/actions-and-controls.md#undo-rather-than-confirm.
 */
export function SaveBar({
  unsaved,
  undoable = false,
  redoable = false,
  saveable,
  busy,
  onUndo,
  onRedo,
  onDiscard,
  onSave,
}: {
  /** There is a change to save, valid or not. */
  unsaved: boolean
  /** Undo and Redo show only where the editor gives `onUndo` and `onRedo`. */
  undoable?: boolean
  redoable?: boolean
  /** Whether Save is on, which the editor decides. */
  saveable: boolean
  /** A save runs. */
  busy: boolean
  onUndo?: () => void
  onRedo?: () => void
  onDiscard: () => void
  onSave: () => void
}): ReactNode {
  const t = useText()
  const { size, colour, spacing } = useDesignTokens()
  const anchorRef = useRef<HTMLDivElement>(null)
  const barRef = useRef<HTMLDivElement>(null)
  const [frame, setFrame] = useState<{ left: number; width: number }>()
  const [discardOpen, setDiscardOpen] = useState(false)
  const shown = unsaved || redoable
  useUnsavedChanges(unsaved)

  // The bar spans the content, which is the nearest main, so it leaves the sidebar uncovered. Proposed, as the review on
  // #3263 found: a fixed bar is placed from the window's edge, or from a transformed ancestor's, such as a
  // design-system frame, so its own origin is taken off, see the review on #3650.
  useLayoutEffect(() => {
    const main = anchorRef.current?.closest('main')
    if (!shown || !main) {
      return
    }
    const measure = () => {
      const bar = barRef.current
      const origin = bar === null ? 0 : bar.getBoundingClientRect().left - (parseFloat(bar.style.left) || 0)
      const box = main.getBoundingClientRect()
      const left = box.left - origin
      const width = box.width
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
  }, [shown])

  if (!shown) {
    return null
  }

  return (
    <>
      <div ref={anchorRef} style={{ height: size.bar }} />
      <Flex
        ref={barRef}
        data-testid="studio-save-bar"
        align="center"
        justify="space-between"
        gap="middle"
        style={{
          position: 'fixed',
          bottom: 0,
          left: frame?.left ?? 0,
          width: frame?.width ?? '100%',
          minHeight: size.bar,
          zIndex: 10,
          boxSizing: 'border-box',
          paddingBlock: spacing.xs,
          paddingInline: spacing.lg,
          background: colour.surface,
          borderTop: `1px solid ${colour.borderSubtle}`,
        }}
      >
        <AxisActionRow align="center">
          <span>{unsaved ? t('shell.studio.saveBar.unsaved') : t('shell.studio.saveBar.noUnsaved')}</span>
          {onUndo && (
            <>
              <AxisButton
                role="icon"
                nameKey="shell.studio.undo"
                icon={<UndoOutlined />}
                aria-keyshortcuts="Control+Z Meta+Z"
                disabled={!undoable || busy}
                onClick={onUndo}
              />
              <AxisButton
                role="icon"
                nameKey="shell.studio.redo"
                icon={<RedoOutlined />}
                aria-keyshortcuts="Control+Shift+Z Meta+Shift+Z"
                disabled={!redoable || busy}
                onClick={onRedo}
              />
            </>
          )}
        </AxisActionRow>
        <AxisActionRow align="center">
          {unsaved && (
            <AxisButton role="secondary" disabled={busy} onClick={() => setDiscardOpen(true)}>
              {t('shell.studio.discard')}
            </AxisButton>
          )}
          <AxisButton role="finish" data-testid="studio-save" disabled={!saveable} onClick={onSave}>
            {t('shell.studio.save')}
          </AxisButton>
        </AxisActionRow>
      </Flex>
      <Modal
        open={discardOpen}
        width={size.dialog.small}
        title={t('shell.studio.discardConfirm')}
        okText={t('shell.studio.discard')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        onOk={() => {
          setDiscardOpen(false)
          onDiscard()
        }}
        onCancel={() => setDiscardOpen(false)}
      />
    </>
  )
}
