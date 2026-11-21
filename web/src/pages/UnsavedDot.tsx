import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/** The dot of a file with unsaved changes, on its tab and on its row of the explorer's tree. */
export function UnsavedDot() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  return (
    <span
      role="img"
      aria-label={t('shell.studio.saveBar.unsaved')}
      data-testid="studio-package-unsaved"
      style={{
        display: 'inline-block',
        flex: 'none',
        width: spacing.sm,
        height: spacing.sm,
        borderRadius: '50%',
        background: colour.text,
      }}
    />
  )
}
