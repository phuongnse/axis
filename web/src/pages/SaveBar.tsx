import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Modal } from 'antd'
import { useState, type ReactNode } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisButton } from '../platform/AxisButton'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useUnsavedChanges } from '../platform/unsavedChanges'

/**
 * The save bar of a Studio resource, see docs/reference/studio-resource-form.md#save-bar. It draws through `ActionBar`
 * as a docked bar, so it stays at the foot of the window across the content, over the footer, and Discard folds into
 * More actions at a narrow width. It is shown while there is something to save or a step can be redone, and it is not
 * drawn otherwise. Discard asks first, because no Undo brings the changes back, see
 * docs/reference/actions-and-controls.md#undo-rather-than-confirm.
 */
export function SaveBar({
  unsaved,
  undoable,
  redoable,
  saveable,
  busy,
  onUndo,
  onRedo,
  onDiscard,
  onSave,
}: {
  /** There is a change to save, valid or not. */
  unsaved: boolean
  undoable: boolean
  redoable: boolean
  /** Whether Save is on, which the editor decides. */
  saveable: boolean
  /** A save runs. */
  busy: boolean
  onUndo: () => void
  onRedo: () => void
  onDiscard: () => void
  onSave: () => void
}): ReactNode {
  const t = useText()
  const { size } = useDesignTokens()
  const [discardOpen, setDiscardOpen] = useState(false)
  const shown = unsaved || redoable
  useUnsavedChanges(unsaved)

  if (!shown) {
    return null
  }

  return (
    <>
      <ActionBar
        docked
        data-testid="studio-save-bar"
        start={
          <>
            <span>{unsaved ? t('shell.studio.saveBar.unsaved') : t('shell.studio.saveBar.noUnsaved')}</span>
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
        }
        fold={
          unsaved
            ? [
                {
                  key: 'discard',
                  labelKey: 'shell.studio.discard',
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
