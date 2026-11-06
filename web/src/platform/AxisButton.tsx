import { DownOutlined } from '@ant-design/icons'
import { Button, Flex, Tooltip, type ButtonProps, type FlexProps } from 'antd'
import { createContext, useContext, useRef, useState, type CSSProperties, type ReactNode, type Ref } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { focusFollowsTab } from './tabFocus'

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

const ActionRowContext = createContext(false)

type IconButtonProps = Extract<AxisButtonProps, { role: 'icon' }>

/** Opens on hover and Tab focus, and a click hides it until the pointer comes back. */
function IconButton(props: IconButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const {
    role: _role,
    nameKey,
    destructive,
    nameSuffix,
    joined,
    icon,
    style,
    disabled,
    onClick,
    onMouseEnter,
    onMouseLeave,
    ...rest
  } = props
  const [open, setOpen] = useState(false)
  const pointerOver = useRef(false)
  const hushed = useRef(false)
  const name = t(nameKey)
  const tint: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
  return (
    <Tooltip
      title={name}
      trigger={['hover', 'focus']}
      open={open}
      onOpenChange={(next) =>
        setOpen(next && !hushed.current && (pointerOver.current || !!disabled || focusFollowsTab()))
      }
    >
      <Button
        {...rest}
        type={joined ? 'default' : 'text'}
        disabled={disabled}
        aria-label={nameSuffix ? `${name} ${nameSuffix}` : name}
        icon={<span aria-hidden>{icon}</span>}
        style={{ ...tint, ...style }}
        onMouseEnter={(event) => {
          pointerOver.current = true
          onMouseEnter?.(event)
        }}
        onMouseLeave={(event) => {
          pointerOver.current = false
          hushed.current = false
          onMouseLeave?.(event)
        }}
        onClick={(event) => {
          hushed.current = true
          setOpen(false)
          onClick?.(event)
        }}
      />
    </Tooltip>
  )
}

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
 * text buttons, `main` in `primaryText` and `destructive` in `dangerText`. `plain` is a borderless text button in the
 * text colour, for header controls such as the account button. `caret` adds a down caret after the text of any text
 * role. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const { colour } = useDesignTokens()
  const inRow = useContext(ActionRowContext)

  if (props.role === 'icon') return <IconButton {...props} />

  const { role, style, disabled, caret, children, ...rest } = props
  const body = {
    ...rest,
    children: caret
      ? [children, <DownOutlined key="caret" aria-hidden style={{ flex: 'none', fontSize: 12 }} />]
      : children,
  }
  const outline = (text: string): CSSProperties => (disabled ? {} : { color: text, borderColor: text })
  const tinted = (text: string): CSSProperties => (disabled ? {} : { color: text })
  const variant = inRow ? 'text' : 'outlined'
  switch (role) {
    case 'finish':
      return <Button {...body} type="primary" disabled={disabled} style={style} />
    case 'main':
      return (
        <Button
          {...body}
          color="primary"
          variant={variant}
          disabled={disabled}
          style={{ ...(inRow ? tinted(colour.primaryText) : outline(colour.primaryText)), ...style }}
        />
      )
    case 'destructive':
      return (
        <Button
          {...body}
          color="danger"
          variant={variant}
          disabled={disabled}
          style={{ ...(inRow ? tinted(colour.dangerText) : outline(colour.dangerText)), ...style }}
        />
      )
    case 'secondary':
      return <Button {...body} type={inRow ? 'text' : 'default'} disabled={disabled} style={style} />
    case 'plain':
      return <Button {...body} type="text" disabled={disabled} style={style} />
  }
}
