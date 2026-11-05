import { DownOutlined } from '@ant-design/icons'
import { Button, Flex, Tooltip, type ButtonProps, type FlexProps } from 'antd'
import { createContext, useContext, useRef, useState, type CSSProperties, type ReactNode, type Ref } from 'react'
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

const ActionRowContext = createContext(false)

/**
 * A row of actions that has a solid `finish` button (D128). Beside it, `main`, `secondary` and `destructive` buttons
 * are text buttons in their own colour, so the finish button is the only filled shape. Proposed: with `wrap` false the
 * row stays on one line, so a band that measures whether its actions fit can fold them.
 */
export function AxisActionRow({
  children,
  justify,
  align,
  wrap = true,
}: {
  children: ReactNode
  justify?: FlexProps['justify']
  align?: FlexProps['align']
  wrap?: boolean
}) {
  return (
    <ActionRowContext.Provider value={true}>
      <Flex gap="small" wrap={wrap} justify={justify} align={align}>
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
  const t = useText()
  const { colour } = useDesignTokens()
  const inRow = useContext(ActionRowContext)

  if (props.role === 'icon') {
    const { role: _role, nameKey, destructive, nameSuffix, joined, icon, style, disabled, ...rest } = props
    const name = t(nameKey)
    const tint: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
    return (
      <NamedIconButton
        {...rest}
        name={name}
        accessibleName={nameSuffix ? `${name} ${nameSuffix}` : name}
        joined={joined}
        disabled={disabled}
        icon={icon}
        style={{ ...tint, ...style }}
      />
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

/**
 * Proposed: the icon button that `AxisButton`'s `icon` role draws, named by a text already in hand rather than a text
 * key, for a page with no text catalogue such as the identity provider's. The name is its tooltip on hover and on
 * keyboard focus and its accessible name. A click closes the tooltip until the pointer comes back, and focus that a
 * click or a closing dialog gives the button opens none, so a tooltip never stays over what the action opened.
 */
export function NamedIconButton({
  name,
  accessibleName,
  joined,
  icon,
  ref,
  onClick,
  onMouseLeave,
  onBlur,
  ...rest
}: Omit<Base, 'children' | 'aria-label'> & {
  name: string
  /** The accessible name when it adds to the tooltip, such as a row number. */
  accessibleName?: string
  joined?: boolean
  icon: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const clicked = useRef(false)
  const button = useRef<HTMLButtonElement | null>(null)

  function openChange(next: boolean) {
    const element = button.current
    const focusedByPointer =
      element !== null &&
      document.activeElement === element &&
      !element.matches(':focus-visible') &&
      !element.matches(':hover')
    if (next && (clicked.current || focusedByPointer)) {
      return
    }
    setOpen(next)
  }

  return (
    // Proposed: the tooltip listens to hover alone, and the button opens it on keyboard focus itself. Ant Design's own
    // focus trigger closes it with flushSync on blur, which a dialog that takes focus as it opens sets off mid-render.
    <Tooltip title={name} trigger={['hover']} open={open} onOpenChange={openChange}>
      <Button
        {...rest}
        ref={(element) => {
          // The icon role always draws a button, never a link.
          const target = element as HTMLButtonElement | null
          button.current = target
          if (typeof ref === 'function') {
            ref(target)
          } else if (ref) {
            ref.current = target
          }
        }}
        type={joined ? 'default' : 'text'}
        aria-label={accessibleName ?? name}
        icon={<span aria-hidden>{icon}</span>}
        onClick={(event) => {
          clicked.current = true
          setOpen(false)
          onClick?.(event)
        }}
        onMouseLeave={(event) => {
          clicked.current = false
          onMouseLeave?.(event)
        }}
        onFocus={(event) => {
          if (!clicked.current && event.currentTarget.matches(':focus-visible')) {
            setOpen(true)
          }
          rest.onFocus?.(event)
        }}
        onBlur={(event) => {
          clicked.current = false
          setOpen(false)
          onBlur?.(event)
        }}
      />
    </Tooltip>
  )
}
