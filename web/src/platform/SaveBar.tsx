import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Modal } from 'antd'
import { useContext, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ActionBar } from './ActionBar'
import { AxisButton } from './AxisButton'
import { ShellBarSlot } from './shellBar'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useUnsavedChanges } from './unsavedChanges'

/**
 * Proposed, see the review on #3650: a site's record form draws its save in this bar too, with no Undo and Redo.
 * The save bar of a Studio resource, see docs/reference/studio-resource-form.md#save-bar. It stays at the foot of the
 * window across the content, right above the footer, and draws through `ActionBar`, so Discard folds into More actions
 * at a narrow width. It is shown while there is something to save or a step can be redone, and it is not
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
  const { size } = useDesignTokens()
  const [discardOpen, setDiscardOpen] = useState(false)
  const slot = useContext(ShellBarSlot)
  const shown = unsaved || redoable
  useUnsavedChanges(unsaved)

  if (!shown) {
    return null
  }

  // Proposed: the bar draws in the shell's place right above the footer, so it never covers the footer and needs no
  // measuring, see the review on #3650. Outside a shell it draws where it stands.
  const bar = (
    <div data-testid="studio-save-bar">
      <ActionBar
        start={
          <>
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
          </>
        }
        fold={
          unsaved
            ? [
                {
                  key: 'discard',
                  labelKey: 'shell.studio.discard',
                  // Proposed: Discard throws the changes away, so it is destructive, see the review on #3650.
                  destructive: true,
                  disabled: busy,
                  onClick: () => setDiscardOpen(true),
                },
              ]
            : []
        }
        end={
          <AxisButton role="finish" data-testid="studio-save" disabled={!saveable} onClick={onSave}>
            {t('shell.studio.save')}
          </AxisButton>
        }
      />
    </div>
  )
  const placed = slot ? createPortal(bar, slot) : bar

  return (
    <>
      {placed}
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
