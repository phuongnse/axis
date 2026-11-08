/** The roles of `AxisButton`, in the order of the role table in docs/reference/actions-and-controls.md#buttons. */
export const buttonRoles = ['finish', 'main', 'secondary', 'icon', 'destructive', 'plain', 'link'] as const

/** The level of an action. Each level has one style, so the same kind of action always looks the same. */
export type ButtonRole = (typeof buttonRoles)[number]
