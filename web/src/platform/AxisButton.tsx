import { Button, Tooltip, type ButtonProps } from 'antd'
import { createContext, useContext, useRef, useState, type CSSProperties, type ReactNode, type Ref } from 'react'
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
