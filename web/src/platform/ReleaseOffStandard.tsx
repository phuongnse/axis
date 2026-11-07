import { Flex, Tag, Typography } from 'antd'
import { nameCompare } from './listOrder'
import type { StudioRelaxedPoint } from './studio'
import { useText } from './texts'

/**
 * The off-standard mark of a release in the release history, in Studio and in the operator console: the Off-standard
 * tag, then one line for each relaxed point with its module, point and reason. It shows nothing for a standard release,
 * which has no relaxed point. With `tagOnly`, which a heading sets because the page lists the points in words, it shows
 * the tag alone. The points are sorted by module and then by point in `locale`. A long reason wraps at 360px, also in a column that does not wrap, so a table that scrolls sideways does not outgrow its card.
 */
export function ReleaseOffStandard({
  relaxed,
  locale,
  tagOnly = false,
}: {
  relaxed: StudioRelaxedPoint[]
  locale: string
  tagOnly?: boolean
}) {
  const t = useText()
  if (relaxed.length === 0) {
    return null
  }
  const compare = nameCompare(locale)
  const tag = (
    // oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token.
    <Tag color="warning" data-testid="release-off-standard">
      {t('shell.studio.offStandard')}
    </Tag>
  )
  if (tagOnly) {
    return tag
  }
  return (
    <Flex vertical gap="small" align="flex-start" style={{ maxWidth: 360, whiteSpace: 'normal' }}>
      {tag}
      {relaxed
        .toSorted((left, right) => compare(left.module, right.module) || compare(left.point, right.point))
        .map((entry) => (
          <Typography.Text key={`${entry.module}/${entry.point}`} data-testid="release-relaxed-point">
            {entry.module} <Typography.Text code>{entry.point}</Typography.Text>: {entry.reason}
          </Typography.Text>
        ))}
    </Flex>
  )
}
