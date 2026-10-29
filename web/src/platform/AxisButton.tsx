import { Button, Tooltip, type ButtonProps } from 'antd'
import type { CSSProperties, ReactNode, Ref } from 'react'
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
    })

/**
 * The one button of Axis (D96). `finish` completes a form or a dialog and is solid primary. `main` is the main
 * action of a page and is an outline in `primaryText`. `secondary` is every other action and is the grey outline.
 * `destructive` removes data and is an outline in `dangerText`. `icon` has no text, so it takes a text key and shows
 * the name as a tooltip on hover and on keyboard focus. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()

  if (props.role === 'icon') {
    const { role: _role, nameKey, destructive, nameSuffix, icon, style, disabled, ...rest } = props
    const name = t(nameKey)
    const tint: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
    return (
      <Tooltip title={name} trigger={['hover', 'focus']}>
        <Button
          {...rest}
          type="text"
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
  switch (role) {
    case 'finish':
      return <Button {...rest} type="primary" disabled={disabled} style={style} />
    case 'main':
      return (
        <Button
          {...rest}
          color="primary"
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(colour.primaryText), ...style }}
        />
      )
    case 'destructive':
      return (
        <Button
          {...rest}
          color="danger"
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(colour.dangerText), ...style }}
        />
      )
    case 'secondary':
      return <Button {...rest} type="default" disabled={disabled} style={style} />
  }
}
