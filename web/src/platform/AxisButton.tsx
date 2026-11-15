import { DownOutlined } from '@ant-design/icons'
import { Button, Dropdown, Flex, Tooltip, type ButtonProps, type FlexProps } from 'antd'
import {
  Children,
  createContext,
  isValidElement,
  useContext,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type MouseEvent,
  type ReactElement,
  type ReactNode,
  type Ref,
} from 'react'
import type { ButtonRole } from './buttonRoles'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { focusFollowsTab } from './tabFocus'

type Base = Omit<ButtonProps, 'type' | 'color' | 'variant' | 'danger' | 'role' | 'icon'> & {
  ref?: Ref<HTMLButtonElement>
}

export type AxisButtonProps =
  | (Base & {
      role: Exclude<ButtonRole, 'icon'>
      icon?: never
      /** A text trigger that opens a menu ends in a caret, see docs/reference/actions-and-controls.md#menus. */
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
          // Enter and Space also click, with detail 0; only a pointer click hushes the tooltip.
          if (event.detail > 0) {
            hushed.current = true
            setOpen(false)
          }
          onClick?.(event)
        }}
      />
    </Tooltip>
  )
}

/**
 * A row of actions that has a solid `finish` button, see docs/reference/actions-and-controls.md#buttons. Beside it,
 * `main`, `secondary` and `destructive` buttons are text buttons in their own colour, so the finish button is the only
 * filled shape. Proposed, see step 20 of the review on #3263: with `fold`, a row whose actions do not fit on one line
 * folds every action but the `finish` one into a More menu before it, in their order, and shows them again once they
 * fit, so the main action and the words beside the row never wrap. Its children are then `AxisButton`s with text.
 */
export function AxisActionRow({
  children,
  justify,
  align,
  fold = false,
}: {
  children: ReactNode
  justify?: FlexProps['justify']
  align?: FlexProps['align']
  fold?: boolean
}) {
  if (fold) {
    return <FoldingActionRow align={align}>{children}</FoldingActionRow>
  }
  return (
    <ActionRowContext.Provider value={true}>
      <Flex gap="small" wrap justify={justify} align={align}>
        {children}
      </Flex>
    </ActionRowContext.Provider>
  )
}

/** The `fold` row of `AxisActionRow`: it takes the room its row leaves it and measures whether its actions fit. */
function FoldingActionRow({ children, align }: { children: ReactNode; align?: FlexProps['align'] }) {
  const t = useText()
  const room = useRef<HTMLDivElement>(null)
  const row = useRef<HTMLDivElement>(null)
  const actions = Children.toArray(children).filter(isValidElement) as ReactElement<
    AxisButtonProps & { role: Exclude<ButtonRole, 'icon'> }
  >[]
  const keys = actions.map((action) => String(action.key)).join('|')
  // The width the actions took when they last did not fit, kept with the actions it was measured for.
  const [folded, setFolded] = useState<{ keys: string; width: number }>()
  const isFolded = folded?.keys === keys

  // The row keeps its actions' own widths, so it is wider than the room it has when they do not fit.
  useLayoutEffect(() => {
    const space = room.current
    const actionsRow = row.current
    if (space === null || actionsRow === null) {
      return
    }
    const measure = () => {
      if (!isFolded) {
        if (actionsRow.offsetWidth > space.clientWidth) {
          setFolded({ keys, width: actionsRow.offsetWidth })
        }
      } else if (space.clientWidth >= folded.width) {
        setFolded(undefined)
      }
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(space)
    return () => observer?.disconnect()
  }, [keys, isFolded, folded])

  const finishing = actions.filter((action) => action.props.role === 'finish')
  const others = actions.filter((action) => action.props.role !== 'finish')
  return (
    <ActionRowContext.Provider value={true}>
      <div ref={room} style={{ display: 'flex', flex: '1 1 0', minWidth: 0 }}>
        <Flex
          ref={row}
          gap="small"
          align={align}
          style={{ flex: 'none', flexWrap: 'nowrap', marginInlineStart: 'auto' }}
        >
          {isFolded && others.length > 0 ? (
            <>
              <Dropdown
                trigger={['click']}
                menu={{
                  items: others.map((action) => ({
                    key: String(action.key),
                    label: action.props.children,
                    disabled: action.props.disabled,
                    danger: action.props.role === 'destructive',
                  })),
                  onClick: ({ key, domEvent }) =>
                    others
                      .find((action) => String(action.key) === key)
                      ?.props.onClick?.(domEvent as MouseEvent<HTMLElement>),
                }}
              >
                <AxisButton role="secondary" caret data-testid="action-row-more">
                  {t('shell.actionRow.more')}
                </AxisButton>
              </Dropdown>
              {finishing}
            </>
          ) : (
            actions
          )}
        </Flex>
      </div>
    </ActionRowContext.Provider>
  )
}

/**
 * The one button of Axis, see docs/reference/actions-and-controls.md#buttons. `finish` completes a form or a dialog and
 * is solid primary. `main` is the main action of a page and is an outline in `primaryText`. `secondary` is every other
 * action and is the grey outline. `destructive` removes data and is an outline in `dangerText`. `icon` has no text, so
 * it takes a text key and shows the name as a tooltip on hover and on keyboard focus. An icon is borderless, unless
 * `joined` draws it as a bordered default button for a control joined to an input. Inside an `AxisActionRow`, `main`,
 * `secondary` and `destructive` are text buttons, `main` in `primaryText` and `destructive` in `dangerText`. `plain` is
 * a borderless text button in the text colour, for header controls such as the account button. `caret` adds a down
 * caret after the text of any text role. `link` is a button drawn as a link, in `primaryText`, for an action that goes to
 * no address. The role is not passed on to the DOM.
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
    case 'link':
      return (
        <Button
          {...body}
          type="link"
          disabled={disabled}
          style={{ ...tinted(colour.primaryText), paddingInline: 0, ...style }}
        />
      )
  }
}
