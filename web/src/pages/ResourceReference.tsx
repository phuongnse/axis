import { Typography } from 'antd'
import { useContext } from 'react'
import { Link } from 'react-router'
import { resourceTitle, type StudioResourceItem } from '../platform/studio'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { FileAtLine } from './FileAtLine'

/**
 * A pointer to a resource, named as people read it: its title, with its kind muted after it. It is a link to `href` when
 * given, and plain text otherwise. Proposed, #4902: a real link with an address, so it opens in a new tab and its address
 * can be copied, see docs/reference/actions-and-controls.md#opening-things. Without a `resource` Studio cannot name the thing, so the pointer is its `file`, drawn
 * by {@link FileAtLine} as muted text, and it opens nothing. See docs/reference/actions-and-controls.md#buttons and the which-widget row of
 * docs/reference/shared-widgets.md#which-widget.
 */
export function ResourceReference({
  file,
  resource,
  href,
}: {
  file: string
  resource: Pick<StudioResourceItem, 'kind' | 'name' | 'titles' | 'locale'> | undefined
  href?: string
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  if (resource === undefined) {
    return <FileAtLine file={file} />
  }
  const title = resourceTitle(resource, locale, t)
  return (
    // Proposed, #4902: inline text, so the reference follows the words before it on their line and wraps as words do.
    <span>
      {href ? <Link to={href}>{title}</Link> : <Typography.Text>{title}</Typography.Text>}
      <Typography.Text style={{ color: colour.textMuted, marginInlineStart: spacing.xs }}>
        {kindSingular(resource.kind, t)}
      </Typography.Text>
    </span>
  )
}
