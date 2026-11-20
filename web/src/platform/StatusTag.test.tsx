import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { colourTokens } from './designTokens'
import { StatusTag, type StatusTone } from './StatusTag'
import { toneIconColours } from './statusTones'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

type ColourName = keyof typeof colourTokens

/** The colour token each tone's text and border take, as the page's shared tones say. */
const tones: Record<StatusTone, { text: ColourName; border: ColourName }> = {
  neutral: { text: 'text', border: 'border' },
  attention: { text: 'warningText', border: 'warningText' },
  success: { text: 'successText', border: 'successText' },
  muted: { text: 'textMuted', border: 'borderSubtle' },
  danger: { text: 'dangerText', border: 'dangerText' },
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
  it.each(['light', 'dark'] as const)('draws each tone in its colour tokens, outlined, in %s mode', (mode) => {
    for (const [tone, { text, border }] of Object.entries(tones) as [StatusTone, (typeof tones)[StatusTone]][]) {
      const { unmount } = render(
        <ThemeModeProvider initialMode={mode}>
          <StatusTag tone={tone} data-testid={`tag-${tone}`}>
            Word
          </StatusTag>
        </ThemeModeProvider>,
      )
      const tag = screen.getByTestId(`tag-${tone}`)

      expect(tag).toHaveAttribute('data-tone', tone)
      expect(tag).toHaveStyle({
        color: colourTokens[text][mode],
        borderColor: colourTokens[border][mode],
        background: 'transparent',
      })
      unmount()
    }
  })

  it('has no margin of its own, so the heading that holds it sets the spacing', () => {
    const tag = renderTag('neutral', 'light')

    expect(tag.style.marginInlineEnd).toBe('0px')
    expect(tag).toHaveTextContent('Word')
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
