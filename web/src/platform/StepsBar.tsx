import { CheckOutlined, ClockCircleOutlined, CloseOutlined, LoadingOutlined, MinusOutlined } from '@ant-design/icons'
import { Tooltip, Typography } from 'antd'
import type { ComponentType, CSSProperties, ReactElement, ReactNode } from 'react'
import { FocusRingStyle } from './FocusRingStyle'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

const numbersFrom = 7
const titleWidth = 160
const circleSize = 24

/** How a step of a feed stands, as docs/reference/ai-authoring-in-studio.md#the-feed gives each mark. */
export type StepMark = 'done' | 'now' | 'waiting' | 'stopped' | 'idle'

/** What a step of a feed shows besides its title: its mark, what it says about itself after its title, and its body. */
export interface StepsBarItem {
  mark: StepMark
  /** Muted, after the title on its line. */
  standing?: ReactNode
  /** Under the title and its standing. */
  body?: ReactNode
}

/** The icon of each mark, outlined as `StateBadge`'s are. */
const markIcons: Record<StepMark, ComponentType<{ spin?: boolean }>> = {
  done: CheckOutlined,
  now: LoadingOutlined,
  waiting: ClockCircleOutlined,
  stopped: CloseOutlined,
  idle: MinusOutlined,
}

/** The mark of a feed's item: a circle with the mark's icon, which assistive technology reads as the mark's word. */
function FeedMark({ mark }: { mark: StepMark }) {
  const t = useText()
  const { colour, size } = useDesignTokens()
  const colours: Record<StepMark, string> = {
    done: colour.primaryText,
    now: colour.primaryText,
    waiting: colour.warningText,
    stopped: colour.danger,
    idle: colour.textMuted,
  }
  const Icon = markIcons[mark]
  return (
    <span
      role="img"
      aria-label={t(`shell.stepsBar.mark.${mark}`)}
      data-mark={mark}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flex: 'none',
        width: size.smallControl,
        height: size.smallControl,
        borderRadius: '50%',
        border: `1px solid ${colours[mark]}`,
        color: colours[mark],
        fontSize: 12,
        lineHeight: 1,
      }}
    >
      <Icon aria-hidden spin={mark === 'now' ? true : undefined} />
    </span>
  )
}

/**
 * One item of a feed: the mark and, except after the last item, a line down to the next, and on the right the title in
 * bold with its standing muted after it, then the body, `spacing-md` above the next item.
 */
function FeedItem({ title, item, last }: { title: string; item: StepsBarItem; last: boolean }) {
  const { colour, size, spacing } = useDesignTokens()
  const active = item.mark === 'now' || item.mark === 'waiting'
  return (
    <li
      aria-current={active ? 'step' : undefined}
      style={{ display: 'flex', alignItems: 'stretch', gap: spacing.sm, width: '100%' }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 'none' }}>
        <FeedMark mark={item.mark} />
        {!last && (
          <span
            aria-hidden
            data-testid="steps-bar-line"
            style={{ flex: 1, width: 1, minHeight: spacing.sm, background: colour.border }}
          />
        )}
      </div>
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: spacing.xs,
          flex: 1,
          minWidth: 0,
          paddingBottom: last ? 0 : spacing.md,
        }}
      >
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            columnGap: spacing.xs,
            minHeight: size.smallControl,
          }}
        >
          <Typography.Text strong>{title}</Typography.Text>
          {item.standing !== undefined && (
            <Typography.Text style={{ color: colour.textMuted }}>{item.standing}</Typography.Text>
          )}
        </div>
        {item.body}
      </div>
    </li>
  )
}

/**
 * The steps of a wizard in a row, or in a column with `vertical`: a numbered circle and a title for each, with a line to
 * the next. A finished step shows a check mark in place of its number. From seven steps, only the current title shows,
 * and the other steps show their number, or a check mark when finished, with the title on hover, on focus and to
 * assistive technology. The current step is marked with `aria-current`, and a row wraps when it does not fit. The bar
 * keeps no space around itself, see docs/reference/shared-widgets.md#the-rule.
 *
 * With `onPick`, each step is a button with no fill that calls `onPick` with the step's index, and the focus ring shows
 * for the keyboard. The button adds no `aria-current`, so the bar keeps the one it has. `entry` wraps each step's button,
 * or its plain title, inside the step's item, so a caller can add a drop target or a mark around it.
 *
 * With `items` and `vertical`, the bar is a feed, see docs/reference/ai-authoring-in-studio.md#the-feed: each step has a
 * mark in a circle, whose accessible name is the mark's word, and a line down to the next step but the last. The title is
 * bold with the standing muted after it, and the body is under them. A step marked now or waiting carries `aria-current`.
 * A feed has no `current`, `onPick` or `entry`.
 */
export function StepsBar({
  titles,
  current = -1,
  vertical = false,
  items,
  onPick,
  entry,
}: {
  titles: string[]
  /** The index of the step that stands now, and the steps before it are finished. Not used with `items`. */
  current?: number
  vertical?: boolean
  /** Draws each step as a feed item. It aligns with `titles` by index and is used only with `vertical`. */
  items?: StepsBarItem[]
  /** Makes each step a button that calls this with the step's index. */
  onPick?: (index: number) => void
  /** Wraps the content of the step at `index`, its button or its plain title, inside the step's item. */
  entry?: (index: number, content: ReactNode) => ReactNode
}) {
  const { colour, spacing } = useDesignTokens()
  if (vertical && items !== undefined) {
    return (
      <ol
        data-testid="steps-bar"
        style={{ display: 'flex', flexDirection: 'column', maxWidth: '100%', listStyle: 'none', margin: 0, padding: 0 }}
      >
        {titles.map((title, index) => (
          <FeedItem key={index} title={title} item={items[index]} last={index === titles.length - 1} />
        ))}
      </ol>
    )
  }
  const numbered = titles.length >= numbersFrom
  const connector: CSSProperties = vertical
    ? {
        display: 'block',
        flex: 'none',
        width: 1,
        height: spacing.sm,
        marginBlock: spacing.xs,
        marginInlineStart: (circleSize - 1) / 2,
        background: colour.border,
      }
    : {
        display: 'inline-block',
        flex: 'none',
        width: 48,
        height: 1,
        marginInline: spacing.xs,
        background: colour.border,
      }
  return (
    <>
      {onPick && <FocusRingStyle selector="[data-step-pick]:focus-visible" />}
      <ol
        data-testid="steps-bar"
        style={{
          display: 'flex',
          flexDirection: vertical ? 'column' : 'row',
          flexWrap: vertical ? 'nowrap' : 'wrap',
          alignItems: vertical ? 'flex-start' : 'center',
          maxWidth: '100%',
          listStyle: 'none',
          margin: 0,
          padding: 0,
        }}
      >
        {titles.map((title, index) => {
          const isCurrent = index === current
          const finished = index < current
          const outline = finished ? colour.primaryText : colour.textMuted
          const mark = finished ? <CheckOutlined aria-hidden /> : index + 1
          const circle: CSSProperties = {
            position: 'relative',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: 'none',
            width: circleSize,
            height: circleSize,
            borderRadius: '50%',
            border: isCurrent ? 'none' : `1px solid ${outline}`,
            background: isCurrent ? colour.primary : 'transparent',
            color: isCurrent ? colour.onPrimary : outline,
            fontSize: 12,
            lineHeight: 1,
          }
          const numberOnly = numbered && !isCurrent
          // A step with a button is focused on the button, so its number is not a Tab stop of its own.
          const badge = numberOnly ? (
            <span style={circle} tabIndex={onPick ? undefined : 0}>
              <span aria-hidden>{mark}</span>
              <span style={visuallyHidden}>{title}</span>
            </span>
          ) : (
            <span aria-hidden style={circle}>
              {mark}
            </span>
          )
          const tooltip = (node: ReactElement) =>
            numberOnly ? (
              <Tooltip title={title} trigger={['hover', 'focus']}>
                {node}
              </Tooltip>
            ) : (
              node
            )
          const label = (
            <span style={{ display: 'flex', alignItems: 'center' }}>
              {onPick ? badge : tooltip(badge)}
              {!numberOnly && (
                <Typography.Text
                  ellipsis={{ tooltip: title }}
                  style={{
                    maxWidth: titleWidth,
                    marginInlineStart: spacing.xs,
                    color: isCurrent ? colour.primaryText : colour.textMuted,
                    fontWeight: isCurrent ? 500 : 400,
                  }}
                >
                  {title}
                </Typography.Text>
              )}
            </span>
          )
          const pickable = onPick
            ? tooltip(
                <button
                  type="button"
                  data-step-pick
                  onClick={() => onPick(index)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    padding: 0,
                    border: 0,
                    background: 'none',
                    font: 'inherit',
                    color: 'inherit',
                    cursor: 'pointer',
                  }}
                >
                  {label}
                </button>,
              )
            : label
          return (
            <li
              key={index}
              aria-current={isCurrent ? 'step' : undefined}
              style={{
                display: 'flex',
                flexDirection: vertical ? 'column' : 'row',
                alignItems: vertical ? 'flex-start' : 'center',
                flex: 'none',
              }}
            >
              {entry ? entry(index, pickable) : pickable}
              {index < titles.length - 1 && <span aria-hidden style={connector} />}
            </li>
          )
        })}
      </ol>
    </>
  )
}
