import { Tag } from 'antd'
import { useContext, type ReactNode } from 'react'
import { statusTints } from './antTheme'
import { ThemeModeContext, useDesignTokens } from './themeMode'

/**
 * Proposed: what a status asks of the person reading it, which sets its tag's colours, see the review on #3263:
 * `neutral` while something runs and needs nobody, `attention` while it waits for a person, `success` when it ended
 * well, `danger` when it failed, and `muted` when it ended with nothing more to do.
 */
export type StatusTone = 'neutral' | 'attention' | 'success' | 'danger' | 'muted'

/**
 * Proposed: a status, the place of a thing in its life, such as a request's state, as a tag, see the review on #3263.
 * Its words always say the status, so its colour is never the only sign; it is never bold and never a link. An enum
 * value that is no status, such as a category, is plain text instead.
 */
export function StatusTag({ tone, children }: { tone: StatusTone; children: ReactNode }) {
  const { colour } = useDesignTokens()
  // Outside the theme's provider it is light, as the design tokens are.
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'
  const tints = statusTints(mode)
  const text: Record<StatusTone, string> = {
    neutral: colour.text,
    attention: colour.warningText,
    success: colour.successText,
    danger: colour.dangerText,
    muted: colour.textMuted,
  }
  return (
    <Tag
      data-tone={tone}
      style={{
        marginInlineEnd: 0,
        color: text[tone],
        background: tints[tone].background,
        borderColor: tints[tone].border,
      }}
    >
      {children}
    </Tag>
  )
}
