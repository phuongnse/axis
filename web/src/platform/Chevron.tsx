import { DownOutlined } from '@ant-design/icons'
import { useDesignTokens } from './themeMode'

/** Where a chevron points: down for open or a menu below, right for closed, up for a panel that opens upwards. */
type ChevronDirection = 'down' | 'right' | 'up'

const turn: Record<ChevronDirection, string | undefined> = {
  down: undefined,
  right: 'rotate(-90deg)',
  up: 'rotate(180deg)',
}

/**
 * Proposed, see #3978: every chevron of the platform, Ant Design's DownOutlined at 12 px in `textMuted`, turned to point,
 * so a caret that opens a menu, a select's arrow, a fold in a tree or a code editor and a panel's collapse read as one
 * sign. A select draws its own arrow at the same size and colour through the theme, and a code editor copies this one
 * into its fold gutter.
 */
export function Chevron({ direction = 'down' }: { direction?: ChevronDirection }) {
  const { colour } = useDesignTokens()
  return (
    <DownOutlined
      aria-hidden
      style={{
        flex: 'none',
        fontSize: 12,
        color: colour.textMuted,
        transform: turn[direction],
        transition: 'transform 0.2s',
      }}
    />
  )
}
