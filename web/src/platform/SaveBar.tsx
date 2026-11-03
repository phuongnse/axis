import { Button, Typography } from 'antd'
import { useContext } from 'react'
import { ShellSidebarContext } from './shellSidebar'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: the one place an editor saves, as Shopify's contextual save bar: a bar that stays at the foot of the window,
 * across the content, while there are changes that are not saved. It says so on the left and holds Discard, a text
 * button, and Save, the solid one, on the right. With nothing to save it is not drawn, so a long form never ends in a
 * Save that the author has to scroll to.
 */
export function SaveBar({
  dirty,
  saving,
  canSave,
  onDiscard,
  onSave,
}: {
  dirty: boolean
  saving: boolean
  canSave: boolean
  onDiscard: () => void
  onSave: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const pad = useContext(ShellSidebarContext)?.contentPadding ?? spacing.lg
  if (!dirty) {
    return null
  }
  return (
    <div
      role="region"
      aria-label={t('shell.studio.saveBar.label')}
      data-testid="studio-save-bar"
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
      <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.saveBar.unsaved')}</Typography.Text>
      <div style={{ flex: 1 }} />
      <Button type="text" disabled={saving} onClick={onDiscard} data-testid="studio-discard">
        {t('shell.studio.saveBar.discard')}
      </Button>
      <Button type="primary" loading={saving} disabled={!canSave} onClick={onSave} data-testid="studio-save">
        {t('shell.studio.save')}
      </Button>
    </div>
  )
}
