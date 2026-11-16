import { Empty } from 'antd'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The empty state of every table and list. Ant Design's own empty text is too faint to meet 4.5:1, so the line is drawn
 * in `textMuted`. Without a `text`, it reads "No data yet.". It keeps no space around itself, so the place that draws
 * it gives the space, see docs/reference/shared-widgets.md#the-rule.
 */
export function EmptyState({ text }: { text?: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  return (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      // Ant Design's empty state sets margins above, below and at the sides. Zero them here, as the callers give the space.
      style={{ marginBlock: 0, marginInline: 0 }}
      description={<span style={{ color: colour.textMuted }}>{text ?? t('shell.empty.noData')}</span>}
    />
  )
}
