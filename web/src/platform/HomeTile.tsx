import { Flex, Typography } from 'antd'
import { useState, type CSSProperties, type ReactNode } from 'react'
import { Link } from 'react-router'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: one tile of the home page, for a tool or an application alike: its mark, its title and one line that says
 * what it is for. With `to` the whole tile is the link; without it, `children` hold the links, such as an application's
 * sites when it has more than one.
 */
export function HomeTile({
  to,
  mark,
  title,
  description,
  children,
}: {
  to?: string
  mark: ReactNode
  title: string
  description?: string
  children?: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  // Proposed: a tile that is a link shows it by its title in `primaryText`, as every link, and changes nothing on hover.
  // Keyboard focus draws the `focusRing` outline.
  const [focused, setFocused] = useState(false)
  const style: CSSProperties = {
    display: 'flex',
    alignItems: 'flex-start',
    gap: spacing.sm,
    minWidth: 0,
    padding: spacing.md,
    background: colour.surface,
    border: `1px solid ${colour.borderSubtle}`,
    borderRadius: radius.base,
    outline: focused ? `2px solid ${colour.focusRing}` : 'none',
    outlineOffset: 2,
  }
  const body = (
    <>
      {mark}
      <Flex vertical style={{ flex: 1, minWidth: 0 }}>
        {/* A title without a line under it is centred on the 32 px mark. */}
        <Typography.Text
          strong
          style={{ color: to ? colour.primaryText : undefined, lineHeight: description ? undefined : '32px' }}
        >
          {title}
        </Typography.Text>
        {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
        {children}
      </Flex>
    </>
  )
  return to ? (
    <Link
      to={to}
      style={style}
      onFocus={(event) => setFocused(event.currentTarget.matches(':focus-visible'))}
      onBlur={() => setFocused(false)}
    >
      {body}
    </Link>
  ) : (
    <div style={style}>{body}</div>
  )
}
