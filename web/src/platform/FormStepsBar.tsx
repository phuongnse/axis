import { CheckOutlined } from '@ant-design/icons'
import { theme, Tooltip, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

const numbersFrom = 7
const titleWidth = 160
const circleSize = 24

/**
 * The steps of a wizard in a row: a numbered circle and a title for each, with a line to the next. A finished step
 * shows a check mark in place of its number. From seven steps, only the current title shows, and the other steps show
 * their number, or a check mark when finished, with the title on hover, on focus and to assistive technology. The
 * current step is marked with `aria-current`, and the bar wraps when it does not fit. Proposed: `vertical` puts one step
 * on each line, joined by a vertical line, for a narrow card such as a sign-up page. The bar keeps no space around
 * it: the screen that places it does.
 */
export function FormStepsBar({
  titles,
  current,
  vertical = false,
}: {
  titles: string[]
  current: number
  vertical?: boolean
}) {
  const { colour, spacing } = useDesignTokens()
  // Proposed: the number on the current step's filled circle takes the colour of a primary button's text.
  const { token } = theme.useToken()
  const numbered = titles.length >= numbersFrom
  return (
    <ol
      data-testid="form-steps"
      style={{
        display: 'flex',
        flexDirection: vertical ? 'column' : 'row',
        flexWrap: 'wrap',
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
          color: isCurrent ? token.colorTextLightSolid : outline,
          fontSize: 12,
          lineHeight: 1,
        }
        return (
          <li
            key={index}
            aria-current={isCurrent ? 'step' : undefined}
            style={{ display: 'flex', flexWrap: vertical ? 'wrap' : 'nowrap', alignItems: 'center', flex: 'none' }}
          >
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
                  color: isCurrent ? colour.primaryText : colour.textMuted,
                  fontWeight: isCurrent ? 500 : 400,
                }}
              >
                {title}
              </Typography.Text>
            )}
            {index < titles.length - 1 && vertical && (
              <span aria-hidden style={{ flexBasis: '100%', display: 'flex' }}>
                <span
                  style={{
                    width: 1,
                    height: spacing.md,
                    marginBlock: spacing.xs / 2,
                    marginInlineStart: circleSize / 2 - 0.5,
                    background: colour.border,
                  }}
                />
              </span>
            )}
            {index < titles.length - 1 && !vertical && (
              <span
                aria-hidden
                style={{
                  display: 'inline-block',
                  flex: 'none',
                  width: 48,
                  height: 1,
                  marginInline: spacing.xs,
                  background: colour.border,
                }}
              />
            )}
          </li>
        )
      })}
    </ol>
  )
}
