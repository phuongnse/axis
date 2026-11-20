import { DownOutlined } from '@ant-design/icons'
import { useDesignTokens } from './themeMode'

/** Where a chevron points: down for open or a menu below, right for closed, up for a panel that opens upwards, left for back. */
export type ChevronPoint = 'down' | 'right' | 'up' | 'left'

// DownOutlined turns clockwise, so each point is a quarter turn on from down.
const rotation: Record<ChevronPoint, number> = { down: 0, left: 90, up: 180, right: 270 }

/**
 * The one chevron of the design system: Ant Design's DownOutlined at the `chevron` size in `textMuted`, turned to point.
 * Every chevron draws through it, see docs/reference/frontend.md#design-tokens-and-previews.
 */
export function Chevron({ point = 'down' }: { point?: ChevronPoint }) {
  const { colour, size } = useDesignTokens()
  return (
    <DownOutlined
      aria-hidden
      rotate={rotation[point]}
      style={{ flex: 'none', fontSize: size.chevron, color: colour.textMuted }}
    />
  )
}
