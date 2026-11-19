import { Tag } from 'antd'
import type { ReactNode } from 'react'
import { toneColours, type StatusTone } from './statusTones'
import { useDesignTokens } from './themeMode'

export type { StatusTone }

/**
 * A status as an outlined tag in one of five tones. It has no margin of its own, so the heading that holds it sets the
 * spacing. Data attributes pass to the tag, and `data-tone` names the tone.
 */
export function StatusTag({
  tone,
  children,
  ...data
}: {
  tone: StatusTone
  children: ReactNode
} & Record<`data-${string}`, string | undefined>) {
  const { colour } = useDesignTokens()
  const { text, border } = toneColours[tone]
  return (
    <Tag
      variant="outlined"
      data-tone={tone}
      {...data}
      style={{
        color: colour[text],
        borderColor: colour[border],
        background: 'transparent',
        marginInlineEnd: 0,
      }}
    >
      {children}
    </Tag>
  )
}
