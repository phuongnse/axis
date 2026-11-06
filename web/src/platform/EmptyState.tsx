import { Empty } from 'antd'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The empty state of every table and list. Ant Design's own empty text is too faint to meet 4.5:1, so the line is drawn
 * in `textMuted`. Without a `text`, it reads "No data yet.".
 */
export function EmptyState({ text }: { text?: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  return (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={<span style={{ color: colour.textMuted }}>{text ?? t('shell.empty.noData')}</span>}
    />
  )
}
