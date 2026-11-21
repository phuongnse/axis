import { Tag } from 'antd'
import type { ReactNode } from 'react'
import { useStatusTints } from './antTheme'
import type { StatusTone } from './statusTones'

export type { StatusTone }

/**
 * A status as a tag in one of five tones, see docs/reference/shared-widgets.md#which-widget. It has no margin of its
 * own, so the heading that holds it sets the spacing. Data attributes pass to the tag, and `data-tone` names the tone.
 * A tag that stands for more than its words, such as a layer's mark, takes `role="note"` and an `aria-label`.
 * Proposed: Ant Design's tinted fill and border of each tone, as step 11 of #3263 agreed, design review #4765.
 */
export function StatusTag({
  tone,
  children,
  role,
  'aria-label': ariaLabel,
  ...data
}: {
  tone: StatusTone
  children: ReactNode
  role?: 'note'
  'aria-label'?: string
} & Record<`data-${string}`, string | undefined>) {
  const { text, fill, border } = useStatusTints()[tone]
  return (
    <Tag
      variant="outlined"
      data-tone={tone}
      role={role}
      aria-label={ariaLabel}
      {...data}
      style={{ color: text, borderColor: border, background: fill, marginInlineEnd: 0 }}
    >
      {children}
    </Tag>
  )
}
