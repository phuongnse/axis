import { Tag } from 'antd'
import type { ReactNode } from 'react'
import type { ColourName } from './designTokens'
import { useDesignTokens } from './themeMode'

/** The five tones of a status, which say what the status asks of the reader. See docs/reference/shared-widgets.md#which-widget. */
export type StatusTone = 'neutral' | 'attention' | 'success' | 'muted' | 'danger'

/** Each tone's text colour and border colour, as tokens. A tag is outlined on a transparent background. */
const toneColours: Record<StatusTone, { text: ColourName; border: ColourName }> = {
  neutral: { text: 'text', border: 'border' },
  attention: { text: 'warningText', border: 'warningText' },
  success: { text: 'successText', border: 'successText' },
  muted: { text: 'textMuted', border: 'borderSubtle' },
  danger: { text: 'dangerText', border: 'dangerText' },
}

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
