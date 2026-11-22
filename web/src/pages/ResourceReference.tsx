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
 * A pointer to a resource, named as people read it: its title, with its kind muted after it, inline with the words before
 * it. The title is a real link to `href` when one is given, which opens in a new tab with Ctrl+click and can be copied, and
 * plain text otherwise. Without a `resource` Studio cannot name the thing, so the pointer is its `file`, drawn by
 * {@link FileAtLine} as muted text, and it opens nothing. See docs/reference/actions-and-controls.md#opening-things and the
 * which-widget row of docs/reference/shared-widgets.md#which-widget.
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
  const { colour } = useDesignTokens()
  if (resource === undefined) {
    return <FileAtLine file={file} />
  }
  const title = resourceTitle(resource, locale, t)
  return (
    <span>
      {href ? <Link to={href}>{title}</Link> : <Typography.Text>{title}</Typography.Text>}{' '}
      <Typography.Text style={{ color: colour.textMuted }}>{kindSingular(resource.kind, t)}</Typography.Text>
    </span>
  )
}
