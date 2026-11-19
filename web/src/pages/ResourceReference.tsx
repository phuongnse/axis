import { Typography } from 'antd'
import { useContext } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { resourceTitle, type StudioResourceItem } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * A pointer to a resource, named as people read it: its title, with its kind muted after it. It is a link when `onOpen`
 * is given, and plain text otherwise. Without a `resource` Studio cannot name the thing, so the pointer is its `file`, in
 * muted text, and it opens nothing. See docs/reference/actions-and-controls.md#buttons and the which-widget row of
 * docs/reference/shared-widgets.md#which-widget.
 */
export function ResourceReference({
  file,
  resource,
  onOpen,
}: {
  file: string
  resource: Pick<StudioResourceItem, 'kind' | 'name' | 'titles' | 'locale'> | undefined
  onOpen?: () => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  if (resource === undefined) {
    return <Typography.Text style={{ color: colour.textMuted }}>{file}</Typography.Text>
  }
  const title = resourceTitle(resource, locale, t)
  return (
    <span style={{ display: 'inline-flex', flexWrap: 'wrap', alignItems: 'baseline', gap: spacing.xs }}>
      {onOpen ? (
        <AxisButton role="link" onClick={onOpen} style={{ height: 'auto', whiteSpace: 'normal', textAlign: 'start' }}>
          {title}
        </AxisButton>
      ) : (
        <Typography.Text>{title}</Typography.Text>
      )}
      <Typography.Text style={{ color: colour.textMuted }}>{kindSingular(resource.kind, t)}</Typography.Text>
    </span>
  )
}
