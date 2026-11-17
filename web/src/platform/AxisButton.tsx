import { DownOutlined, EllipsisOutlined } from '@ant-design/icons'
import { Button, Dropdown, Flex, Space, Tooltip, type ButtonProps, type FlexProps } from 'antd'
import {
  createContext,
  useContext,
  useLayoutEffect,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
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
      /**
       * Proposed, see the review on #3650: a `link` inside a line of text or a value, such as a step a run came from,
       * takes the line's height and no padding, as a link in text does, so it sits on the text's spacing.
       */
      inline?: boolean
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
 * An action of an `AxisActionRow` that folds, see docs/reference/actions-and-controls.md#buttons. It is a secondary
 * button, or a menu entry once the row folds. `labelKey` is the text key of its text, `attributes` go to its button.
 */
export interface AxisFoldAction {
  key: string
  labelKey: string
  disabled?: boolean
  /** Proposed, see the review on #3650: an action that throws something away, a `destructive` button or a danger entry. */
  destructive?: boolean
  onClick: () => void
  attributes?: object
}

/** Justifies the row like `justify`, but starts the row at its start edge when the content is wider, so it can be measured. */
function safeJustify(justify: FlexProps['justify']): CSSProperties['justifyContent'] {
  return justify === 'flex-end' || justify === 'end' || justify === 'center' ? `safe ${justify}` : undefined
}

/**
 * A row of actions that has a solid `finish` button, see docs/reference/actions-and-controls.md#buttons. Beside it,
 * `main`, `secondary` and `destructive` buttons are text buttons in their own colour, so the finish button is the only
 * filled shape. The row wraps. A row with `fold` keeps one line instead: it draws `children`, then the `fold` actions
 * as secondary buttons, then `end`. When its content is wider than the row, the `fold` actions become the entries of
 * one "…" icon menu named More actions, in their order. They are buttons again when the row is as wide as the content
 * was before it folded.
 */
export function AxisActionRow({
  children,
  fold,
  end,
  justify,
  align,
}: {
  children?: ReactNode
  fold?: AxisFoldAction[]
  end?: ReactNode
  justify?: FlexProps['justify']
  align?: FlexProps['align']
}) {
  const t = useText()
  const rowRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<() => void>(() => {})
  const needed = useRef(0)
  const foldedKeys = useRef('')
  const [folded, setFolded] = useState(false)
  const keys = fold?.map((action) => action.key).join('\n') ?? ''

  // The row measures itself after every draw and when it resizes, the way a table measures its width.
  useLayoutEffect(() => {
    measureRef.current = () => {
      const row = rowRef.current
      if (!row || !fold) {
        return
      }
      if (foldedKeys.current !== keys) {
        // The actions changed, so unfold to measure them.
        foldedKeys.current = keys
        if (folded) {
          setFolded(false)
          return
        }
      }
      if (!folded && row.scrollWidth > row.clientWidth) {
        needed.current = row.scrollWidth
        setFolded(true)
      } else if (folded && row.clientWidth >= needed.current) {
        setFolded(false)
      }
    }
    measureRef.current()
  })
  const folds = fold !== undefined
  useEffect(() => {
    const row = rowRef.current
    if (!folds || !row || typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(() => measureRef.current())
    observer.observe(row)
    return () => observer.disconnect()
  }, [folds])

  const rowProps = fold
    ? {
        ref: rowRef,
        'data-fold-row': true,
        wrap: false,
        style: { flex: '1 1 auto', minWidth: 0, justifyContent: safeJustify(justify) },
      }
    : { wrap: true }
  return (
    <ActionRowContext.Provider value={true}>
      <Flex gap="small" justify={justify} align={align} {...rowProps}>
        {children}
        {fold &&
          (folded ? (
            <Dropdown
              trigger={['click']}
              autoFocus
              menu={{
                items: fold.map((action) => ({
                  key: action.key,
                  label: t(action.labelKey),
                  disabled: action.disabled,
                  danger: action.destructive,
                  onClick: action.onClick,
                })),
                'aria-label': t('shell.actionRow.more'),
              }}
            >
              <AxisButton role="icon" nameKey="shell.actionRow.more" icon={<EllipsisOutlined />} />
            </Dropdown>
          ) : (
            fold.map((action) => (
              <AxisButton
                key={action.key}
                role={action.destructive ? 'destructive' : 'secondary'}
                disabled={action.disabled}
                onClick={action.onClick}
                {...action.attributes}
              >
                {t(action.labelKey)}
              </AxisButton>
            ))
          ))}
        {end}
      </Flex>
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
 * no address; `inline` gives it a line of text's height, for a link inside text or a value. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const { colour } = useDesignTokens()
  const inRow = useContext(ActionRowContext)

  if (props.role === 'icon') return <IconButton {...props} />

  const { role, style, disabled, caret, inline, children, ...rest } = props
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
          style={{
            ...tinted(colour.primaryText),
            paddingInline: 0,
            ...(inline
              ? { height: 'auto', padding: 0, border: 0, lineHeight: 'inherit', verticalAlign: 'baseline' }
              : {}),
            ...style,
          }}
        />
      )
  }
}

/** One action of an `AxisButtonGroup`: its text, and what its button takes. */
export interface AxisGroupAction {
  key: string
  label: string
  loading?: boolean
  disabled?: boolean
  onClick: () => void
}

/**
 * Proposed, see the review on #3650: actions of equal weight that together are the main action of a region, such as a
 * task's outcomes, joined in one group in the `main` role, in the order given. The region keeps one main action, and no
 * action of the group stands above another: each takes the width of the widest. Each action stays a button of its own,
 * with no selected state, and the group is named by `nameKey`. A group of one action is a plain `main` button.
 */
export function AxisButtonGroup({ nameKey, actions }: { nameKey: string; actions: readonly AxisGroupAction[] }) {
  const t = useText()
  const buttons = actions.map(({ key, label, ...button }) => (
    <AxisButton role="main" key={key} {...button}>
      {label}
    </AxisButton>
  ))
  if (buttons.length < 2) {
    return <>{buttons}</>
  }
  // An inline grid of equal columns gives every action the width of the widest, so none looks weightier.
  return (
    <Space.Compact
      role="group"
      aria-label={t(nameKey)}
      style={{ display: 'inline-grid', gridAutoFlow: 'column', gridAutoColumns: '1fr' }}
    >
      {buttons}
    </Space.Compact>
  )
}
