import { Badge, type BadgeProps } from 'antd'

/**
 * Proposed, see the review on #3263: the one count on an icon button, such as the bell's unread notices and a table's
 * filter conditions: Ant Design's small badge as it draws it, placed at the icon's top right corner with `offset`, and
 * "9+" past nine. No count shows nothing. What wraps it, such as a popover that opens from the button, reaches the
 * badge through the props it passes on.
 */
export function CountBadge({
  count,
  children,
  ...rest
}: Omit<BadgeProps, 'count' | 'overflowCount' | 'size' | 'offset'> & { count: number }) {
  return (
    <Badge {...rest} count={count} overflowCount={9} size="small" offset={[-7, 7]}>
      {children}
    </Badge>
  )
}
