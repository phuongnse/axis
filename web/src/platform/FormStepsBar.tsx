import { Tooltip, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

const numbersFrom = 7
const titleWidth = 160
const circleSize = 24

/**
 * The steps of a wizard in a row: a numbered circle and a title for each, with a line to the next. From seven steps,
 * only the current title shows, and the other steps show their number with the title on hover, on focus and to
 * assistive technology. The current step is marked with `aria-current`, and the bar wraps when it does not fit.
 */
export function FormStepsBar({ titles, current }: { titles: string[]; current: number }) {
  const { colour, spacing } = useDesignTokens()
  const numbered = titles.length >= numbersFrom
  return (
    <ol
      data-testid="form-steps"
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        maxWidth: '100%',
        listStyle: 'none',
        margin: 0,
        padding: 0,
        marginBottom: spacing.lg,
      }}
    >
      {titles.map((title, index) => {
        const isCurrent = index === current
        const outline = index < current ? colour.primaryText : colour.textMuted
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
          color: isCurrent ? colour.surface : outline,
          fontSize: 12,
          lineHeight: 1,
        }
        return (
          <li
            key={index}
            aria-current={isCurrent ? 'step' : undefined}
            style={{ display: 'flex', alignItems: 'center', flex: 'none' }}
          >
            {numbered && !isCurrent ? (
              <Tooltip title={title} trigger={['hover', 'focus']}>
                <span tabIndex={0} style={circle}>
                  <span aria-hidden>{index + 1}</span>
                  <span style={visuallyHidden}>{title}</span>
                </span>
              </Tooltip>
            ) : (
              <span aria-hidden style={circle}>
                {index + 1}
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
            {index < titles.length - 1 && (
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
