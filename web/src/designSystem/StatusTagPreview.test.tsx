import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { colourTokens } from '../platform/designTokens'
import { ThemeModeProvider } from '../platform/theme'
import { StatusTagPreview } from './StatusTagPreview'

describe('StatusTagPreview', () => {
  it.each(['light', 'dark'] as const)('draws every tone on surface and on canvas in %s mode', (mode) => {
    render(
      <ThemeModeProvider initialMode={mode}>
        <StatusTagPreview />
      </ThemeModeProvider>,
    )

    for (const ground of ['surface', 'canvas'] as const) {
      const block = screen.getByTestId(`status-tag-${ground}`)
      expect(block).toHaveStyle({ background: colourTokens[ground][mode] })
      const tones = within(block)
        .getAllByText(/./)
        .map((tag) => tag.getAttribute('data-tone'))
      expect(tones).toEqual(['neutral', 'attention', 'success', 'muted', 'danger'])
    }
  })
})
