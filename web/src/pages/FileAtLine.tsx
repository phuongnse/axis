import { Typography } from 'antd'
import { Link } from 'react-router'
import { fillText } from '../platform/fillText'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * A file at its line, such as a problem's file: the file's name, with ", line N" after it when `line` is given. It is a
 * link to `href` when given, and muted text otherwise. Proposed, #4902: a real link, whose address opens the file at the
 * line in the package's workspace, see docs/reference/actions-and-controls.md#opening-things. See docs/reference/actions-and-controls.md#buttons and the
 * which-widget row of docs/reference/shared-widgets.md#which-widget.
 */
export function FileAtLine({ file, line, href }: { file: string; line?: number; href?: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const text = line === undefined ? file : fillText(t('shell.studio.problems.fileAtLine'), { file, line })
  return href ? (
    <Link to={href}>{text}</Link>
  ) : (
    <Typography.Text style={{ color: colour.textMuted }}>{text}</Typography.Text>
  )
}
