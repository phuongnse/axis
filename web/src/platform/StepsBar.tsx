import { CheckOutlined, CloseOutlined, MinusOutlined } from '@ant-design/icons'
import { Tooltip, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

const numbersFrom = 7
const titleWidth = 160
const circleSize = 24

/**
 * The steps of a wizard in a row, or in a column with `vertical`: a numbered circle and a title for each, with a line to
 * the next. A finished step shows a check mark in place of its number. From seven steps, only the current title shows,
 * and the other steps show their number, or a check mark when finished, with the title on hover, on focus and to
 * assistive technology. The current step is marked with `aria-current`, and a row wraps when it does not fit. The bar
 * keeps no space around itself, see docs/reference/shared-widgets.md#the-rule. Proposed, see the review on #3263: with
 * `stopped`, the current step is where the run stopped, and its words say so to assistive technology after its title:
 * with the `danger` tone, as a failure, a cross in `danger` and its title in `dangerText`; with the `muted` tone, as
 * when someone stops it, a dash and its title in `textMuted`.
 */
export function StepsBar({
  titles,
  current,
  vertical = false,
  stopped,
}: {
  titles: string[]
  current: number
  vertical?: boolean
  /** The current step is where the run stopped: its words, such as "Failed", and its tone. */
  stopped?: { words: string; tone: 'danger' | 'muted' }
}) {
  const { colour, spacing } = useDesignTokens()
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
        const halted = isCurrent ? stopped : undefined
        const failed = halted?.tone === 'danger'
        const muted = halted?.tone === 'muted'
        const active = isCurrent && !muted
        const finished = index < current
        const outline = finished ? colour.primaryText : colour.textMuted
        const mark = finished ? (
          <CheckOutlined aria-hidden />
        ) : failed ? (
          <CloseOutlined aria-hidden />
        ) : muted ? (
          <MinusOutlined aria-hidden />
        ) : (
          index + 1
        )
        const circle: CSSProperties = {
          position: 'relative',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          flex: 'none',
          width: circleSize,
          height: circleSize,
          borderRadius: '50%',
          border: active ? 'none' : `1px solid ${outline}`,
          background: failed ? colour.danger : active ? colour.primary : 'transparent',
          color: active ? colour.onPrimary : outline,
          fontSize: 12,
          lineHeight: 1,
        }
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
            <span style={{ display: 'flex', alignItems: 'center' }}>
              {numbered && !isCurrent ? (
                <Tooltip title={title} trigger={['hover', 'focus']}>
                  <span tabIndex={0} style={circle}>
                    <span aria-hidden>{mark}</span>
                    <span style={visuallyHidden}>{title}</span>
                  </span>
                </Tooltip>
              ) : (
                <span aria-hidden style={circle}>
                  {mark}
                </span>
              )}
              {(!numbered || isCurrent) && (
                <Typography.Text
                  ellipsis={{ tooltip: title }}
                  style={{
                    maxWidth: titleWidth,
                    marginInlineStart: spacing.xs,
                    color: failed ? colour.dangerText : active ? colour.primaryText : colour.textMuted,
                    fontWeight: isCurrent ? 500 : 400,
                  }}
                >
                  {title}
                  {halted !== undefined && <span style={visuallyHidden}>{` ${halted.words}`}</span>}
                </Typography.Text>
              )}
            </span>
            {index < titles.length - 1 && <span aria-hidden style={connector} />}
          </li>
        )
      })}
    </ol>
  )
}
