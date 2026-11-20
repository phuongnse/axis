import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed, see #3978: the dot that marks a package file with unsaved changes, on its tab and on its row in the
 * explorer, named for assistive technology.
 */
export function UnsavedDot({ inline = false }: { inline?: boolean }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  return (
    <span
      role="img"
      aria-label={t('shell.studio.package.unsaved')}
      style={{
        display: 'inline-block',
        flex: 'none',
        width: 6,
        height: 6,
        borderRadius: '50%',
        background: colour.primary,
        ...(inline ? { marginInlineStart: spacing.xs, verticalAlign: 'middle' } : {}),
      }}
    />
  )
}
