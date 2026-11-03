import { DownOutlined } from '@ant-design/icons'
import { Button, Tooltip, type ButtonProps } from 'antd'
import type { CSSProperties, ReactNode, Ref } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The level of an action. Each level has one style, so the same kind of action always looks the same. */
export type ButtonRole = 'finish' | 'main' | 'secondary' | 'icon' | 'destructive' | 'plain'

type Base = Omit<ButtonProps, 'type' | 'color' | 'variant' | 'danger' | 'role' | 'icon'> & {
  ref?: Ref<HTMLButtonElement>
}

export type AxisButtonProps =
  | (Base & {
      role: Exclude<ButtonRole, 'icon'>
      icon?: never
      /** A text trigger that opens a menu ends in a caret (D128). */
      caret?: boolean
    })
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
 * default button for a control joined to an input. `plain` is a borderless text button in the text colour, for header
 * controls such as the account button. `caret` adds a down caret after the text of any text role. The role is not
 * passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()

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

  const { role, style, disabled, caret, children, ...rest } = props
  const body = {
    ...rest,
    children: caret
      ? [children, <DownOutlined key="caret" aria-hidden style={{ flex: 'none', fontSize: 12 }} />]
      : children,
  }
  const outline = (text: string): CSSProperties => (disabled ? {} : { color: text, borderColor: text })
  switch (role) {
    case 'finish':
      return <Button {...body} type="primary" disabled={disabled} style={style} />
    case 'main':
      return (
        <Button
          {...body}
          color="primary"
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(colour.primaryText), ...style }}
        />
      )
    case 'destructive':
      return (
        <Button
          {...body}
          color="danger"
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(colour.dangerText), ...style }}
        />
      )
    case 'secondary':
      return <Button {...body} type="default" disabled={disabled} style={style} />
    case 'plain':
      return <Button {...body} type="text" disabled={disabled} style={style} />
  }
}
