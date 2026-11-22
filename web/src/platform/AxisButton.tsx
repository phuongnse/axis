import { EllipsisOutlined } from '@ant-design/icons'
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
import { useSelectedItemTint } from './antTheme'
import { Chevron } from './Chevron'
import type { ButtonRole } from './buttonRoles'
import { fillText } from './fillText'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { menuOf, type MenuAction } from './menuActions'
import { focusFollowsTab } from './tabFocus'

type Base = Omit<ButtonProps, 'type' | 'color' | 'variant' | 'danger' | 'role' | 'icon'> & {
  ref?: Ref<HTMLButtonElement>
}

/** What a chosen subtle button says: `true` or `false` for a button of a list of choices, or where it leads. */
type Selected = boolean | 'page' | 'step'

/**
 * `destructive` is the tone any role but `link` takes, and `selected` marks a chosen subtle button, see
 * docs/reference/actions-and-controls.md#buttons.
 */
type TextButton =
  | {
      role: Exclude<ButtonRole, 'subtle' | 'link'>
      destructive?: boolean
      selected?: never
    }
  | {
      role: 'subtle'
      destructive?: boolean
      selected?: Selected
    }
  | {
      role: 'link'
      destructive?: never
      selected?: never
    }

export type AxisButtonProps =
  | (Base &
      TextButton & {
        icon?: never
        nameKey?: never
        /** A text trigger that opens a menu ends in a caret, see docs/reference/actions-and-controls.md#menus. */
        caret?: boolean
      })
  | (Omit<Base, 'children' | 'aria-label'> & {
      role: 'subtle'
      icon: ReactNode
      /** The text key of the button's name, which is its tooltip and its accessible name. */
      nameKey: string
      /** The values of the `{name}` placeholders in the text of `nameKey`, which fill the tooltip and the accessible name. */
      nameValues?: Record<string, string>
      /** Draws the icon in `dangerText`. */
      destructive?: boolean
      /** Draws the button on the selected tint in `primaryText` and sets `aria-pressed`, or `aria-current` for a string. */
      selected?: Selected
      /** Appended to the accessible name only, such as a row number. The tooltip stays the name. */
      nameSuffix?: string
      /** Draws a bordered default button, to sit joined to an input in a Space.Compact. */
      joined?: boolean
      /**
       * The text key of what the button says right after it did its work, such as Copied. While it is set, the tooltip
       * opens with that text, even after a pointer click. The accessible name stays the name. The caller clears it to go
       * back to normal.
       */
      doneKey?: string
    })

const ActionRowContext = createContext(false)

type IconButtonProps = Extract<AxisButtonProps, { nameKey: string }>

function isIconButton(props: AxisButtonProps): props is IconButtonProps {
  return props.nameKey !== undefined
}

type Marks = { 'aria-pressed'?: boolean | 'true' | 'false' | 'mixed'; 'aria-current'?: unknown }

/**
 * What `selected` gives a subtle button: the selected tint behind `primaryText`, and the one attribute that says so, in
 * place of any `aria-pressed` or `aria-current` the caller passes. Without `selected`, the caller's attributes stay.
 */
function withSelected<T extends Marks>(
  selected: Selected | undefined,
  rest: T,
  tint: string,
  text: string,
  disabled?: boolean,
): {
  rest: Omit<T, 'aria-pressed' | 'aria-current'> & { 'aria-pressed'?: boolean; 'aria-current'?: 'page' | 'step' }
  style: CSSProperties
} {
  if (selected === undefined) {
    return { rest: rest as never, style: {} }
  }
  const { 'aria-pressed': _pressed, 'aria-current': _current, ...others } = rest
  const mark = typeof selected === 'boolean' ? { 'aria-pressed': selected } : { 'aria-current': selected }
  return {
    rest: { ...others, ...mark },
    style: selected ? { background: tint, ...(disabled ? {} : { color: text }) } : {},
  }
}

/** Opens on hover and Tab focus, and a click hides it until the pointer comes back, except to say `doneKey`. */
function IconButton(props: IconButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const {
    role: _role,
    nameKey,
    nameValues,
    destructive,
    selected,
    nameSuffix,
    joined,
    doneKey,
    icon,
    style,
    disabled,
    onClick,
    onMouseEnter,
    onMouseLeave,
    ...attributes
  } = props
  const tint = useSelectedItemTint()
  const chosen = withSelected(selected, attributes, tint, colour.primaryText, disabled)
  const [open, setOpen] = useState(false)
  const [shownDone, setShownDone] = useState(doneKey)
  // The click that did the work has hushed the tooltip, so saying it is done opens it again.
  if (shownDone !== doneKey) {
    setShownDone(doneKey)
    if (doneKey !== undefined) {
      setOpen(true)
    }
  }
  const pointerOver = useRef(false)
  const hushed = useRef(false)
  const name = nameValues ? fillText(t(nameKey), nameValues) : t(nameKey)
  const tone: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
  return (
    <Tooltip
      title={doneKey === undefined ? name : t(doneKey)}
      trigger={['hover', 'focus']}
      open={open}
      onOpenChange={(next) =>
        setOpen(next && !hushed.current && (pointerOver.current || !!disabled || focusFollowsTab()))
      }
    >
      <Button
        {...chosen.rest}
        type={joined ? 'default' : 'text'}
        disabled={disabled}
        aria-label={nameSuffix ? `${name} ${nameSuffix}` : name}
        icon={<span aria-hidden>{icon}</span>}
        style={{ ...tone, ...chosen.style, ...style }}
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
  onClick: () => void
  attributes?: object
}

/** Justifies the row like `justify`, but starts the row at its start edge when the content is wider, so it can be measured. */
function safeJustify(justify: FlexProps['justify']): CSSProperties['justifyContent'] {
  return justify === 'flex-end' || justify === 'end' || justify === 'center' ? `safe ${justify}` : undefined
}

/**
 * A row of actions that has a solid `finish` button, see docs/reference/actions-and-controls.md#buttons. Beside it,
 * every other action is a subtle button, so the finish button is the only filled shape, except a `main` action, which
 * keeps its border in `primaryText`. The row wraps. A row with `fold` of two or more actions keeps one line instead: it
 * draws `children`, then the `fold` actions as secondary buttons, then `end`. When its content is wider than the row,
 * the `fold` actions become the entries of one "…" icon menu named More actions, in their order. They are buttons again
 * when the row is as wide as the content was before it folded. A row with one `fold` action wraps instead, as a menu of
 * one entry is never drawn. With `outlined`, the row keeps those buttons outlined, as a bar at the foot of one pane does,
 * because it has no `finish` button beside them.
 */
export function AxisActionRow({
  children,
  fold,
  end,
  justify,
  align,
  outlined,
}: {
  children?: ReactNode
  fold?: AxisFoldAction[]
  end?: ReactNode
  justify?: FlexProps['justify']
  align?: FlexProps['align']
  outlined?: boolean
}) {
  const t = useText()
  const rowRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<() => void>(() => {})
  const needed = useRef(0)
  const foldedKeys = useRef('')
  const [folded, setFolded] = useState(false)
  const keys = fold?.map((action) => action.key).join('\n') ?? ''
  // A menu of one entry is never drawn, so fewer than two fold actions wrap.
  const folding = (fold?.length ?? 0) > 1

  // The row measures itself after every draw and when it resizes, the way a table measures its width.
  useLayoutEffect(() => {
    measureRef.current = () => {
      const row = rowRef.current
      if (!row || !folding) {
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
  useEffect(() => {
    const row = rowRef.current
    if (!folding || !row || typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(() => measureRef.current())
    observer.observe(row)
    return () => observer.disconnect()
  }, [folding])

  const rowProps = {
    ref: fold ? rowRef : undefined,
    'data-fold-row': folding || undefined,
    wrap: !folding,
    style: fold ? { flex: '1 1 auto', minWidth: 0, justifyContent: safeJustify(justify) } : undefined,
  }
  return (
    <ActionRowContext.Provider value={!outlined}>
      <Flex gap="small" justify={justify} align={align} {...rowProps}>
        {children}
        {fold &&
          (folding && folded ? (
            <Dropdown
              trigger={['click']}
              autoFocus
              menu={{
                items: menuOf(fold.map((action) => ({ ...action, label: t(action.labelKey) }))),
                'aria-label': t('shell.actionRow.more'),
              }}
            >
              <AxisButton role="subtle" nameKey="shell.actionRow.more" icon={<EllipsisOutlined />} />
            </Dropdown>
          ) : (
            fold.map((action) => (
              <AxisButton
                key={action.key}
                role="secondary"
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
 * The actions of a title row, such as a section's or a field group's, each drawn in its own role, see
 * docs/reference/shell.md#layout. Unlike an `AxisActionRow`, it draws no action as a text button, so a section's Add
 * keeps its border and a tool stays subtle.
 */
export function TitleRowActions({ children }: { children: ReactNode }) {
  return (
    <ActionRowContext.Provider value={false}>
      <Flex gap="small" align="center" wrap>
        {children}
      </Flex>
    </ActionRowContext.Provider>
  )
}

/**
 * The one button of Axis, see docs/reference/actions-and-controls.md#buttons. The role is the weight. `finish` completes
 * a form or a dialog and is solid primary. `main` is the main action of a region and is an outline in `primaryText`,
 * even beside a finish button. `secondary` is every other action and is the grey outline. `subtle` is a borderless text
 * button in the text colour, for tools and header controls. With an `icon` and a `nameKey` it is an icon-only button,
 * which has no text, so it shows the name as a tooltip on hover and on keyboard focus. An icon is borderless, unless
 * `joined` draws it as a bordered default button for a control joined to an input. The `destructive` tone, which any role
 * but `link` takes, says the action removes something: `main`, `secondary` and `subtle` draw in `dangerText`, and
 * `finish` is a solid `danger` fill with white text. `selected` draws a subtle button on the selected tint in
 * `primaryText`, and sets the one `aria-pressed`, or `aria-current` for `"page"` and `"step"`, in place of any the caller
 * passes. Inside an `AxisActionRow`, `secondary` and `subtle` are text buttons, and a destructive `secondary` is in
 * `dangerText`. `caret` adds a down caret after the text of any text role. `link` is a button drawn
 * as a link, in `primaryText`, for an action that goes to no address. The role is not passed on to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const { colour } = useDesignTokens()
  const tint = useSelectedItemTint()
  const inRow = useContext(ActionRowContext)

  if (isIconButton(props)) return <IconButton {...props} />

  const { role, style, disabled, caret, children, destructive, selected, ...attributes } = props
  const chosen = withSelected(selected, attributes, tint, colour.primaryText, disabled)
  const body = {
    ...chosen.rest,
    children: caret ? [children, <Chevron key="caret" />] : children,
  }
  const outline = (text: string): CSSProperties => (disabled ? {} : { color: text, borderColor: text })
  const tinted = (text: string): CSSProperties => (disabled ? {} : { color: text })
  switch (role) {
    case 'finish':
      return destructive ? (
        <Button
          {...body}
          type="primary"
          danger
          disabled={disabled}
          style={
            disabled
              ? style
              : { background: colour.danger, borderColor: colour.danger, color: colour.onPrimary, ...style }
          }
        />
      ) : (
        <Button {...body} type="primary" disabled={disabled} style={style} />
      )
    case 'main':
      // A region's main action keeps its border wherever it sits, even beside a finish button.
      return (
        <Button
          {...body}
          color={destructive ? 'danger' : 'primary'}
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(destructive ? colour.dangerText : colour.primaryText), ...style }}
        />
      )
    case 'secondary':
      if (!destructive) {
        return <Button {...body} type={inRow ? 'text' : 'default'} disabled={disabled} style={style} />
      }
      return (
        <Button
          {...body}
          color="danger"
          variant={inRow ? 'text' : 'outlined'}
          disabled={disabled}
          style={{ ...(inRow ? tinted(colour.dangerText) : outline(colour.dangerText)), ...style }}
        />
      )
    case 'subtle':
      return destructive ? (
        <Button
          {...body}
          color="danger"
          variant="text"
          disabled={disabled}
          style={{ ...tinted(colour.dangerText), ...chosen.style, ...style }}
        />
      ) : (
        <Button {...body} type="text" disabled={disabled} style={{ ...chosen.style, ...style }} />
      )
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

/**
 * Actions of equal weight as one joined group, see docs/reference/actions-and-controls.md#buttons: buttons of one
 * `role` in Ant Design's `Space.Compact`, each as wide as the widest, named by the text of `nameKey` for assistive
 * technology. A group of one is a plain button of that role, with no group.
 */
export function AxisButtonGroup({
  role,
  nameKey,
  actions,
}: {
  role: 'main' | 'secondary'
  nameKey: string
  actions: readonly {
    key: string
    label: string
    loading?: boolean
    disabled?: boolean
    onClick: () => void
  }[]
}) {
  const t = useText()
  const buttons = actions.map((action) => (
    <AxisButton
      key={action.key}
      role={role}
      loading={action.loading}
      disabled={action.disabled}
      onClick={action.onClick}
    >
      {action.label}
    </AxisButton>
  ))
  if (actions.length === 1) {
    return buttons[0]
  }
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

/**
 * Actions that manage one thing as one split button, see docs/reference/actions-and-controls.md#buttons. The text of
 * `action` does it, and the joined caret opens `menu`, in its order, a destructive entry in `dangerText`. They are one
 * shape in Ant Design's `Space.Compact`. `nameSuffix` goes after the names of the text and of the caret for assistive
 * technology, such as the name of the item the actions manage.
 */
export function AxisSplitButton({
  action,
  menu,
  nameSuffix,
}: {
  action: MenuAction
  menu: readonly MenuAction[]
  nameSuffix?: string
}) {
  const t = useText()
  return (
    <Space.Compact>
      <AxisButton
        role="secondary"
        aria-label={nameSuffix ? `${action.label} ${nameSuffix}` : undefined}
        disabled={action.disabled}
        onClick={action.onClick}
        {...action.attributes}
      >
        {action.label}
      </AxisButton>
      <Dropdown trigger={['click']} autoFocus menu={{ items: menuOf(menu), 'aria-label': t('shell.actionRow.more') }}>
        <AxisButton role="subtle" joined nameKey="shell.actionRow.more" nameSuffix={nameSuffix} icon={<Chevron />} />
      </Dropdown>
    </Space.Compact>
  )
}
