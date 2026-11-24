import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Modal } from 'antd'
import { useState, type ReactNode } from 'react'
import { ActionBar } from './ActionBar'
import { AxisButton } from './AxisButton'
import { DialogFooter } from './DialogFooter'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { ShellBarSlot } from './shellBarSlot'
import { useUnsavedChanges } from './unsavedChanges'

/**
 * The save bar of an editor, see docs/reference/studio-resource-form.md#save-bar. It draws through `ActionBar` in
 * `ShellBarSlot`, so it stays at the foot of the window and rests above the footer. At a narrow width its actions wrap
 * rather than fold, as Discard is its only action to fold and a menu of one entry is never drawn, see
 * docs/reference/actions-and-controls.md#buttons. It draws Undo and Redo only when the editor gives `history`. It is shown while there is something
 * to save or, with `history`, a step can be redone, and it is not drawn otherwise. Discard asks first, because no Undo
 * brings the changes back, see docs/reference/actions-and-controls.md#undo-rather-than-confirm. Discard is in the
 * destructive tone, see docs/reference/studio-resource-form.md#save-bar. `data-testid` names the bar and `saveTestId`
 * its Save button.
 */
export function SaveBar({
  unsaved,
  history,
  saveable,
  busy,
  onDiscard,
  onSave,
  'data-testid': testId,
  saveTestId,
}: {
  /** There is a change to save, valid or not. */
  unsaved: boolean
  /** Undo and Redo, which the editor gives together. Without it the bar draws neither. */
  history?: { undoable: boolean; redoable: boolean; onUndo: () => void; onRedo: () => void }
  /** Whether Save is on, which the editor decides. */
  saveable: boolean
  /** A save runs. */
  busy: boolean
  onDiscard: () => void
  onSave: () => void
  'data-testid'?: string
  saveTestId?: string
}): ReactNode {
  const t = useText()
  const { size, colour } = useDesignTokens()
  const [discardOpen, setDiscardOpen] = useState(false)
  const shown = unsaved || (history?.redoable ?? false)
  useUnsavedChanges(unsaved)

  if (!shown) {
    return null
  }

  return (
    <>
      <ShellBarSlot>
        <ActionBar
          data-testid={testId}
          start={
            <>
              <span style={{ color: colour.text }}>
                {unsaved ? t('shell.studio.saveBar.unsaved') : t('shell.studio.saveBar.noUnsaved')}
              </span>
              {history && (
                <>
                  <AxisButton
                    role="subtle"
                    nameKey="shell.studio.undo"
                    icon={<UndoOutlined />}
                    aria-keyshortcuts="Control+Z Meta+Z"
                    disabled={!history.undoable || busy}
                    onClick={history.onUndo}
                  />
                  <AxisButton
                    role="subtle"
                    nameKey="shell.studio.redo"
                    icon={<RedoOutlined />}
                    aria-keyshortcuts="Control+Shift+Z Meta+Shift+Z"
                    disabled={!history.redoable || busy}
                    onClick={history.onRedo}
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
                    destructive: true,
                    disabled: busy,
                    onClick: () => setDiscardOpen(true),
                  },
                ]
              : []
          }
          end={
            <AxisButton role="finish" data-testid={saveTestId} disabled={!saveable} onClick={onSave}>
              {t('shell.studio.save')}
            </AxisButton>
          }
        />
      </ShellBarSlot>
      <Modal
        open={discardOpen}
        width={size.dialog.small}
        title={t('shell.studio.discardConfirm')}
        footer={
          <DialogFooter
            destructive
            finishText={t('shell.studio.discard')}
            cancelText={t('shell.studio.cancel')}
            onFinish={() => {
              setDiscardOpen(false)
              onDiscard()
            }}
            onCancel={() => setDiscardOpen(false)}
          />
        }
        onCancel={() => setDiscardOpen(false)}
      />
    </>
  )
}
