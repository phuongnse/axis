import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Flex, Modal } from 'antd'
import { useContext, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AxisActionRow, AxisButton } from './AxisButton'
import { useText } from './texts'
import { ShellBarSlot } from './shellBar'
import { useDesignTokens } from './themeMode'
import { useUnsavedChanges } from './unsavedChanges'

/**
 * Proposed, see the review on #3650: a site's record form draws its save in this bar too, with no Undo and Redo.
 * The save bar of a Studio resource, see docs/reference/studio-resource-form.md#save-bar. It stays at the foot of the
 * window across the content, right above the footer, while there is something to save or a step can be redone, and it is not
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
  const slot = useContext(ShellBarSlot)
  const [discardOpen, setDiscardOpen] = useState(false)
  const shown = unsaved || redoable
  useUnsavedChanges(unsaved)

  if (!shown) {
    return null
  }

  const bar = (
    <Flex
      data-testid="studio-save-bar"
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
  )

  return (
    <>
      {/* Proposed: the bar draws in the shell's place for it, right above the footer, see the review on #3650. */}
      {slot ? createPortal(bar, slot) : bar}
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
