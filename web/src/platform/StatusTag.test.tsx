import { render, screen } from '@testing-library/react'
import { ConfigProvider, theme } from 'antd'
import { describe, expect, it } from 'vitest'
import { antConfigFor, antThemeFor } from './antTheme'
import { StatusTag, type StatusTone } from './StatusTag'
import { toneIconColours } from './statusTones'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

const toneNames: StatusTone[] = ['neutral', 'attention', 'success', 'muted', 'danger']

/** What each tone draws from Ant Design's tokens of a theme: the words, the fill and the border. */
function expectedTints(tokens: ReturnType<typeof theme.getDesignToken>) {
  return {
    neutral: { color: tokens.colorText, background: tokens.colorFillSecondary, borderColor: tokens.colorFill },
    attention: {
      color: tokens.colorWarningText,
      background: tokens.colorWarningBg,
      borderColor: tokens.colorWarningBorder,
    },
    success: {
      color: tokens.colorSuccessText,
      background: tokens.colorSuccessBg,
      borderColor: tokens.colorSuccessBorder,
    },
    muted: { color: tokens.colorTextSecondary, background: 'transparent', borderColor: tokens.colorFill },
    danger: { color: tokens.colorErrorText, background: tokens.colorErrorBg, borderColor: tokens.colorErrorBorder },
  }
}

function renderTag(tone: StatusTone, mode: ThemeMode) {
  render(
    <ThemeModeProvider initialMode={mode}>
      <StatusTag tone={tone} data-testid="tag">
        Word
      </StatusTag>
    </ThemeModeProvider>,
  )
  return screen.getByTestId('tag')
}

describe('StatusTag', () => {
  it.each(['light', 'dark'] as const)('draws each tone as a tint of the theme in use, in %s mode', (mode) => {
    const expected = expectedTints(theme.getDesignToken(antThemeFor(mode)))
    for (const tone of toneNames) {
      const { unmount } = render(
        <ThemeModeProvider initialMode={mode}>
          <StatusTag tone={tone} data-testid={`tag-${tone}`}>
            Word
          </StatusTag>
        </ThemeModeProvider>,
      )
      const tag = screen.getByTestId(`tag-${tone}`)

      expect(tag).toHaveAttribute('data-tone', tone)
      expect(tag).toHaveStyle(expected[tone])
      unmount()
    }
  })

  it("tints with a page's seed colour", () => {
    const page = { '--warning': '#0050b3' }
    const config = antConfigFor('light', page)
    render(
      <ConfigProvider {...config}>
        <StatusTag tone="attention" data-testid="tag">
          Word
        </StatusTag>
      </ConfigProvider>,
    )
    const tag = screen.getByTestId('tag')
    const seeded = expectedTints(theme.getDesignToken(antThemeFor('light', page))).attention
    const standard = expectedTints(theme.getDesignToken(antThemeFor('light'))).attention

    expect(seeded.background).not.toBe(standard.background)
    expect(tag).toHaveStyle(seeded)
  })

  it('has no margin of its own, so the heading that holds it sets the spacing', () => {
    const tag = renderTag('neutral', 'light')

    expect(tag.style.marginInlineEnd).toBe('0px')
    expect(tag).toHaveTextContent('Word')
  })

  it('passes a role and an accessible name to the tag', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <StatusTag tone="neutral" role="note" aria-label="supplier, added by this application">
          Added
        </StatusTag>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('note', { name: 'supplier, added by this application' })).toHaveTextContent('Added')
  })

  it('gives each tone its icon colour', () => {
    expect(toneIconColours).toEqual({
      neutral: 'infoText',
      attention: 'warningText',
      success: 'successText',
      danger: 'dangerText',
      muted: 'textMuted',
    })
  })
})
