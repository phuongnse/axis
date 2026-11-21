import { StatusTag } from '../platform/StatusTag'
import type { StatusTone } from '../platform/statusTones'
import { useDesignTokens } from '../platform/themeMode'

/** Each tone with a status it stands for, from the AI request's states. */
const samples: readonly { tone: StatusTone; words: string }[] = [
  { tone: 'neutral', words: 'Working' },
  { tone: 'attention', words: 'Awaiting approval' },
  { tone: 'success', words: 'Accepted' },
  { tone: 'danger', words: 'Failed' },
  { tone: 'muted', words: 'Dropped' },
]

/**
 * Every tone of `StatusTag`, on `surface` and on `canvas`, so its contrast shows on both.
 * Proposed: the statusTag preview, design review #4765.
 */
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
            gap: spacing.sm,
          }}
        >
          <div style={{ color: colour.textMuted }}>On {ground}</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: spacing.sm }}>
            {samples.map(({ tone, words }) => (
              <StatusTag key={tone} tone={tone}>
                {words}
              </StatusTag>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}
