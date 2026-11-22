/** The roles of `AxisButton`, in the order of the role table in docs/reference/actions-and-controls.md#buttons. */
export const buttonRoles = ['finish', 'main', 'secondary', 'subtle', 'link'] as const

/**
 * The weight of an action: `finish` finishes a task, `main` is the main action of a region, `secondary` is most
 * actions and `subtle` is a tool. Each weight has one style, so the same kind of action always looks the same. The
 * destructive tone is an option beside the role, not a role, see docs/reference/actions-and-controls.md#buttons.
 */
export type ButtonRole = (typeof buttonRoles)[number]
