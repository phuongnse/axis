import { Button, Tooltip, type ButtonProps } from 'antd'
import { createContext, useContext, type CSSProperties, type ReactNode, type Ref } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: a row of actions that holds a `finish` button. Beside it, every other action is a text button in its own
 * level's colour, so the solid button is the row's one filled shape. A form's footer and a page's compact band are
 * such rows; a dialog's Cancel follows the same rule through the theme.
 */
const BesideFinish = createContext(false)

export function ActionRow({ finish, children }: { finish: boolean; children: ReactNode }) {
  return <BesideFinish.Provider value={finish}>{children}</BesideFinish.Provider>
}

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

/**
 * The one button of Axis (D96). `finish` completes a form or a dialog and is solid primary. `main` is the main
 * action of a page and is an outline in `primaryText`. `secondary` is every other action and is the grey outline.
 * `destructive` removes data and is an outline in `dangerText`. `icon` has no text, so it takes a text key and shows
 * the name as a tooltip on hover and on keyboard focus. An icon is borderless, unless `joined` draws it as a bordered
 * default button for a control joined to an input. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const besideFinish = useContext(BesideFinish)

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
  if (besideFinish && role !== 'finish') {
    const text = role === 'main' ? colour.primaryText : role === 'destructive' ? colour.dangerText : undefined
    return (
      <Button
        {...rest}
        type="text"
        disabled={disabled}
        style={{ ...(text && !disabled ? { color: text } : {}), ...style }}
      />
    )
  }
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
