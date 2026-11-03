import { Empty } from 'antd'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: what every empty table and list shows, the same everywhere: Ant Design's simple empty picture and one line
 * in textMuted, "No data yet." unless the list says something the reader needs, such as that a release follows every
 * contract. A field with no value keeps Not set, and a cell the dash.
 */
export function EmptyState({ text, testId }: { text?: string; testId?: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  return (
    <Empty
      data-testid={testId}
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={<span style={{ color: colour.textMuted }}>{text ?? t('shell.empty.noData')}</span>}
    />
  )
}
