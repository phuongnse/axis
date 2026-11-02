import { Flex, Tag, Typography } from 'antd'
import type { StudioRelaxedPoint } from './studio'
import { useText } from './texts'

/**
 * The off-standard mark of a release in the release history, in Studio and in the operator console: the Off-standard
 * tag, then one line for each relaxed point with its module, point and reason. It shows nothing for a standard release,
 * which has no relaxed point. A long reason wraps at 360px, so a table that scrolls sideways does not outgrow its card.
 */
export function ReleaseOffStandard({ relaxed }: { relaxed: StudioRelaxedPoint[] }) {
  const t = useText()
  if (relaxed.length === 0) {
    return null
  }
  return (
    <Flex vertical gap="small" align="flex-start" style={{ maxWidth: 360 }}>
      <Tag color="warning" data-testid="release-off-standard">
        {t('shell.studio.offStandard')}
      </Tag>
      {relaxed.map((entry) => (
        <Typography.Text key={`${entry.module}/${entry.point}`} data-testid="release-relaxed-point">
          {entry.module} <Typography.Text code>{entry.point}</Typography.Text>: {entry.reason}
        </Typography.Text>
      ))}
    </Flex>
  )
}
