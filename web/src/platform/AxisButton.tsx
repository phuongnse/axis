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

export type AxisButtonProps =
  | (Base & {
      role: Exclude<ButtonRole, 'icon'>
      icon?: never
      /** A text trigger that opens a menu ends in a caret, see docs/reference/actions-and-controls.md#menus. */
      caret?: boolean
      /**
       * Proposed, #4902: the destructive tone, `dangerText`, for an action that removes. A tone of the main, secondary
       * and subtle roles, never of the finish button, see docs/reference/actions-and-controls.md#buttons.
       */
      destructive?: boolean
    })
  | (Omit<Base, 'children' | 'aria-label'> & {
      role: 'icon'
      icon: ReactNode
      /** The text key of the button's name, which is its tooltip and its accessible name. */
      nameKey: string
      /** The values of the `{name}` placeholders in the text of `nameKey`, which fill the tooltip and the accessible name. */
      nameValues?: Record<string, string>
      /** Draws the icon in `dangerText`. */
      destructive?: boolean
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

type IconButtonProps = Extract<AxisButtonProps, { role: 'icon' }>

/** Opens on hover and Tab focus, and a click hides it until the pointer comes back, except to say `doneKey`. */
function IconButton(props: IconButtonProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const {
    role: _role,
    nameKey,
    nameValues,
    destructive,
    nameSuffix,
    joined,
    doneKey,
    icon,
    style,
    disabled,
    onClick,
    onMouseEnter,
    onMouseLeave,
    ...rest
  } = props
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
  const tint: CSSProperties = destructive && !disabled ? { color: colour.dangerText } : {}
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
 * was before it folded. With `outlined`, the row keeps those buttons outlined, as a bar at the foot of one pane does,
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
    <ActionRowContext.Provider value={!outlined}>
      <Flex gap="small" justify={justify} align={align} {...rowProps}>
        {children}
        {fold &&
          (folded ? (
            <Dropdown
              trigger={['click']}
              autoFocus
              menu={{
                items: menuOf(fold.map((action) => ({ ...action, label: t(action.labelKey) }))),
                'aria-label': t('shell.actionRow.more'),
              }}
            >
              <AxisButton role="icon" nameKey="shell.actionRow.more" icon={<EllipsisOutlined />} />
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
 * The one button of Axis, see docs/reference/actions-and-controls.md#buttons. Proposed, #4902: four roles, by weight.
 * `finish` completes a form or a dialog and is solid primary. `main` is the main action of a region and is an outline in
 * `primaryText`, which it keeps wherever it sits, a title row included. `secondary` is most actions and is the grey
 * outline. `subtle` is a tool, such as Refresh or Undo, and is borderless in the text colour, with a light fill on hover.
 * `icon` is a subtle button with only an icon, so it takes a text key and shows the name as a tooltip on hover and on
 * keyboard focus; `joined` draws it bordered, for a control joined to an input. `destructive` is a tone of `main`,
 * `secondary` and `subtle`: `dangerText` for an action that removes. Inside an `AxisActionRow` beside a finish button,
 * `main` and `secondary` are borderless in the text colour, or in `dangerText` when destructive, so the finish button is
 * the only filled shape and no button looks like a link. `caret` adds a down caret after the text of any text role.
 * `link` is a button drawn as a link, in `primaryText`, for an action that goes to no address. The role is not passed on
 * to the DOM.
 */
export function AxisButton(props: AxisButtonProps) {
  const { colour } = useDesignTokens()
  const inRow = useContext(ActionRowContext)

  if (props.role === 'icon') return <IconButton {...props} />

  const { role, style, disabled, caret, destructive, children, ...rest } = props
  const body = {
    ...rest,
    children: caret ? [children, <Chevron key="caret" />] : children,
  }
  const outline = (text: string): CSSProperties => (disabled ? {} : { color: text, borderColor: text })
  const tinted = (text: string): CSSProperties => (disabled ? {} : { color: text })
  const danger = destructive === true
  // Borderless beside a finish button, and always for a tool: the text colour, or `dangerText` for the destructive tone.
  const borderless = (
    <Button
      {...body}
      type="text"
      disabled={disabled}
      style={{ ...(danger ? tinted(colour.dangerText) : {}), ...style }}
    />
  )
  switch (role) {
    case 'finish':
      return <Button {...body} type="primary" disabled={disabled} style={style} />
    case 'main':
      return inRow ? (
        borderless
      ) : (
        <Button
          {...body}
          variant="outlined"
          disabled={disabled}
          style={{ ...outline(danger ? colour.dangerText : colour.primaryText), ...style }}
        />
      )
    case 'secondary':
      return inRow ? (
        borderless
      ) : (
        <Button
          {...body}
          type="default"
          disabled={disabled}
          style={{ ...(danger ? outline(colour.dangerText) : {}), ...style }}
        />
      )
    case 'subtle':
      return borderless
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
        <AxisButton role="icon" joined nameKey="shell.actionRow.more" nameSuffix={nameSuffix} icon={<Chevron />} />
      </Dropdown>
    </Space.Compact>
  )
}
