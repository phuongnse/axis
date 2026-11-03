import { Button, Flex, Tooltip, type ButtonProps, type FlexProps } from 'antd'
import { createContext, useContext, type CSSProperties, type ReactNode, type Ref } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The level of an action. Each level has one style, so the same kind of action always looks the same. */
export type ButtonRole = 'finish' | 'main' | 'secondary' | 'icon' | 'destructive'

type Base = Omit<ButtonProps, 'type' | 'color' | 'variant' | 'danger' | 'role' | 'icon'> & {
  ref?: Ref<HTMLButtonElement>
}

export type AxisButtonProps =
  | (Base & { role: Exclude<ButtonRole, 'icon'>; icon?: never })
  | (Omit<Base, 'children' | 'aria-label'> & {
      role: 'icon'
      icon: ReactNode
      /** The text key of the button's name, which is its tooltip and its accessible name. */
      nameKey: string
      /** Draws the icon in `dangerText`. */
      destructive?: boolean
      /** Appended to the accessible name only, such as a row number. The tooltip stays the name. */
      nameSuffix?: string
      /** Draws a bordered default button, to sit joined to an input in a Space.Compact. */
      joined?: boolean
    })

const ActionRowContext = createContext(false)

/**
 * A row of actions that has a solid `finish` button (D128). Beside it, `main`, `secondary` and `destructive` buttons
 * are text buttons in their own colour, so the finish button is the only filled shape.
 */
export function AxisActionRow({
  children,
  justify,
  align,
}: {
  children: ReactNode
  justify?: FlexProps['justify']
  align?: FlexProps['align']
}) {
  return (
    <ActionRowContext.Provider value={true}>
      <Flex gap="small" wrap justify={justify} align={align}>
        {children}
      </Flex>
    </ActionRowContext.Provider>
  )
}

/**
 * The one button of Axis (D96). `finish` completes a form or a dialog and is solid primary. `main` is the main
 * action of a page and is an outline in `primaryText`. `secondary` is every other action and is the grey outline.
 * `destructive` removes data and is an outline in `dangerText`. `icon` has no text, so it takes a text key and shows
 * the name as a tooltip on hover and on keyboard focus. An icon is borderless, unless `joined` draws it as a bordered
 * default button for a control joined to an input. Inside an `AxisActionRow`, `main`, `secondary` and `destructive` are
 * text buttons, `main` in `primaryText` and `destructive` in `dangerText`. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const inRow = useContext(ActionRowContext)

  if (props.role === 'icon') {
    const { role: _role, nameKey, destructive, nameSuffix, joined, icon, style, disabled, ...rest } = props
    const name = t(nameKey)
    const tint: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
    return (
      <Tooltip title={name} trigger={['hover', 'focus']}>
        <Button
          {...rest}
          type={joined ? 'default' : 'text'}
          disabled={disabled}
          aria-label={nameSuffix ? `${name} ${nameSuffix}` : name}
          icon={<span aria-hidden>{icon}</span>}
          style={{ ...tint, ...style }}
        />
      </Tooltip>
    )
  }

  const { role, style, disabled, ...rest } = props
  const outline = (text: string): CSSProperties => (disabled ? {} : { color: text, borderColor: text })
  const tinted = (text: string): CSSProperties => (disabled ? {} : { color: text })
  const variant = inRow ? 'text' : 'outlined'
  switch (role) {
    case 'finish':
      return <Button {...rest} type="primary" disabled={disabled} style={style} />
    case 'main':
      return (
        <Button
          {...rest}
          color="primary"
          variant={variant}
          disabled={disabled}
          style={{ ...(inRow ? tinted(colour.primaryText) : outline(colour.primaryText)), ...style }}
        />
      )
    case 'destructive':
      return (
        <Button
          {...rest}
          color="danger"
          variant={variant}
          disabled={disabled}
          style={{ ...(inRow ? tinted(colour.dangerText) : outline(colour.dangerText)), ...style }}
        />
      )
    case 'secondary':
      return <Button {...rest} type={inRow ? 'text' : 'default'} disabled={disabled} style={style} />
  }
}
