import { RedoOutlined, UndoOutlined } from '@ant-design/icons'
import { Button, Modal, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from './AxisButton'
import { dialogWidth } from './dialogButtons'
import { ShellSidebarContext } from './shellSidebarContext'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: the one place an editor saves, as Shopify's contextual save bar: a bar that stays at the foot of the window,
 * across the content, while there are changes that are not saved, or a step undone that can be redone. On the left it
 * says what is unsaved, then Undo and Redo, icons with their keys, Ctrl+Z and Ctrl+Shift+Z, which step through every
 * change, a removed row among them; on the right Discard, a text button, and Save, the solid one. Discard asks first,
 * as it drops every unsaved change at once and no Undo brings them back. With nothing to save or redo it is not drawn.
 */
export function SaveBar({
  dirty,
  saving,
  canSave,
  canUndo = false,
  canRedo = false,
  onUndo,
  onRedo,
  onDiscard,
  onSave,
}: {
  dirty: boolean
  saving: boolean
  canSave: boolean
  canUndo?: boolean
  canRedo?: boolean
  onUndo?: () => void
  onRedo?: () => void
  onDiscard: () => void
  onSave: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const pad = useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
  const [confirming, setConfirming] = useState(false)
  if (!dirty && !canRedo) {
    return null
  }
  return (
    <div
      role="region"
      aria-label={t('shell.studio.saveBar.label')}
      data-testid="studio-save-bar"
      data-save-bar
      style={{
        position: 'sticky',
        bottom: 0,
        zIndex: 9,
        margin: `0 -${pad}px`,
        padding: `${spacing.sm}px ${pad}px`,
        display: 'flex',
        alignItems: 'center',
        gap: spacing.xs,
        background: colour.surface,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Typography.Text style={{ color: colour.textMuted }}>
        {t(dirty ? 'shell.studio.saveBar.unsaved' : 'shell.studio.saveBar.clean')}
      </Typography.Text>
      {onUndo && (
        <AxisButton
          role="icon"
          nameKey="shell.studio.undo"
          icon={<UndoOutlined />}
          disabled={!canUndo}
          onClick={onUndo}
          aria-keyshortcuts="Control+Z Meta+Z"
          data-testid="studio-undo"
        />
      )}
      {onRedo && (
        <AxisButton
          role="icon"
          nameKey="shell.studio.redo"
          icon={<RedoOutlined />}
          disabled={!canRedo}
          onClick={onRedo}
          aria-keyshortcuts="Control+Shift+Z Meta+Shift+Z"
          data-testid="studio-redo"
        />
      )}
      <div style={{ flex: 1 }} />
      {dirty && (
        <Button type="text" disabled={saving} onClick={() => setConfirming(true)} data-testid="studio-discard">
          {t('shell.studio.saveBar.discard')}
        </Button>
      )}
      <Button type="primary" loading={saving} disabled={!dirty || !canSave} onClick={onSave} data-testid="studio-save">
        {t('shell.studio.save')}
      </Button>
      <Modal
        open={confirming}
        width={dialogWidth('small')}
        title={t('shell.studio.saveBar.discardTitle')}
        okText={t('shell.studio.saveBar.discard')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        onOk={() => {
          setConfirming(false)
          onDiscard()
        }}
        onCancel={() => setConfirming(false)}
      >
        <Typography.Text>{t('shell.studio.saveBar.discardBody')}</Typography.Text>
      </Modal>
    </div>
  )
}
