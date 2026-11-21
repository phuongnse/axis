import { Tag } from 'antd'
import { useContext, type ReactNode } from 'react'
import { statusTints } from '../platform/antTheme'
import { StatusTag } from '../platform/StatusTag'
import { toneColours, type StatusTone } from '../platform/statusTones'
import { ThemeModeContext, useDesignTokens } from '../platform/themeMode'

/** Each tone with a status it stands for, from the AI request's states. */
const samples: readonly { tone: StatusTone; words: string }[] = [
  { tone: 'neutral', words: 'Working' },
  { tone: 'attention', words: 'Awaiting approval' },
  { tone: 'success', words: 'Accepted' },
  { tone: 'danger', words: 'Failed' },
  { tone: 'muted', words: 'Dropped' },
]

type Look = 'fillAndBorder' | 'fill' | 'outlined'

/**
 * Proposed: the looks the owner compares in design review #4765. `fillAndBorder` is `StatusTag` itself. The other two are drawn
 * here only for the comparison and go when the owner has chosen.
 */
function ComparedTag({ look, tone, children }: { look: Look; tone: StatusTone; children: ReactNode }) {
  const { colour } = useDesignTokens()
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'
  if (look === 'fillAndBorder') {
    return <StatusTag tone={tone}>{children}</StatusTag>
  }
  const { text, border } = toneColours[tone]
  const tint = statusTints(mode)[tone]
  const style =
    look === 'fill'
      ? { color: colour[text], borderColor: tone === 'muted' ? tint.border : tint.fill, background: tint.fill }
      : { color: colour[text], borderColor: colour[border], background: statusTints(mode).muted.fill }
  return (
    <Tag variant="outlined" style={{ ...style, marginInlineEnd: 0 }}>
      {children}
    </Tag>
  )
}

const looks: readonly { look: Look; title: string }[] = [
  { look: 'fillAndBorder', title: 'Proposed, as step 11 of #3263: tinted fill and border' },
  { look: 'fill', title: 'Tinted fill, no border' },
  { look: 'outlined', title: 'On main today: outlined' },
]

/** Every tone of `StatusTag` in each compared look, on `surface` and on `canvas`. */
export function StatusTagPreview() {
  const { colour, spacing } = useDesignTokens()
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: spacing.md }}>
      {(['surface', 'canvas'] as const).map((ground) => (
        <div
          key={ground}
          data-ground={ground}
          style={{
            background: colour[ground],
            border: `1px solid ${colour.borderSubtle}`,
            padding: spacing.md,
            display: 'grid',
            gap: spacing.md,
          }}
        >
          <div style={{ color: colour.textMuted }}>On {ground}</div>
          {looks.map(({ look, title }) => (
            <div key={look} style={{ display: 'grid', gap: spacing.xs }}>
              <div style={{ color: colour.text }}>{title}</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: spacing.sm }}>
                {samples.map(({ tone, words }) => (
                  <ComparedTag key={tone} look={look} tone={tone}>
                    {words}
                  </ComparedTag>
                ))}
              </div>
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
