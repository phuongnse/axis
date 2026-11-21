import { Tag } from 'antd'
import type { ReactNode } from 'react'
import { useStatusTints } from './antTheme'
import type { StatusTone } from './statusTones'

export type { StatusTone }

/**
 * Any tag on any screen, whether a status, a mark or a value's name: a filled, tinted tag in one of five tones. It has
 * no margin of its own, so the heading that holds it sets the spacing. Data attributes, `role` and `aria-label` pass
 * to the tag, and `data-tone` names the tone. See docs/reference/shared-widgets.md#which-widget.
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
  role?: string
  'aria-label'?: string
} & Record<`data-${string}`, string | undefined>) {
  const { text, fill, border } = useStatusTints()[tone]
  return (
    <Tag
      data-tone={tone}
      role={role}
      aria-label={ariaLabel}
      {...data}
      style={{ color: text, background: fill, borderColor: border, marginInlineEnd: 0 }}
    >
      {children}
    </Tag>
  )
}
