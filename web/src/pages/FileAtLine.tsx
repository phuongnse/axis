import { Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { fillText } from '../platform/fillText'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * A file at its line, such as a problem's file: the file's name, with ", line N" after it when `line` is given. It is a
 * link when `onOpen` is given, and muted text otherwise. See docs/reference/actions-and-controls.md#buttons and the
 * which-widget row of docs/reference/shared-widgets.md#which-widget.
 */
export function FileAtLine({ file, line, onOpen }: { file: string; line?: number; onOpen?: () => void }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const text = line === undefined ? file : fillText(t('shell.studio.problems.fileAtLine'), { file, line })
  return onOpen ? (
    <AxisButton role="link" onClick={onOpen} style={{ height: 'auto', whiteSpace: 'normal', textAlign: 'start' }}>
      {text}
    </AxisButton>
  ) : (
    <Typography.Text style={{ color: colour.textMuted }}>{text}</Typography.Text>
  )
}
