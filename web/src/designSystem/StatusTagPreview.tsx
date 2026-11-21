import { Flex } from 'antd'
import { StatusTag } from '../platform/StatusTag'
import type { StatusTone } from '../platform/statusTones'
import { useDesignTokens } from '../platform/themeMode'

const tones: readonly StatusTone[] = ['neutral', 'attention', 'success', 'muted', 'danger']

/** The `statusTag` card: one tag of each tone on `surface` and on `canvas`, its words the tone's name. */
export function StatusTagPreview() {
  const { colour, spacing } = useDesignTokens()
  return (
    <Flex vertical gap="middle">
      {(['surface', 'canvas'] as const).map((ground) => (
        <Flex
          key={ground}
          gap="small"
          wrap
          align="center"
          data-testid={`status-tag-${ground}`}
          style={{ background: colour[ground], padding: spacing.md }}
        >
          {tones.map((tone) => (
            <StatusTag key={tone} tone={tone}>
              {tone}
            </StatusTag>
          ))}
        </Flex>
      ))}
    </Flex>
  )
}
