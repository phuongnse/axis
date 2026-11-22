import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { fields } from './fixtures'
import { FieldPreview } from './FieldPreview'

const catalogs = [{ texts: platformEnglish, fallbackTexts: platformEnglish }]

function renderPreview(problem?: Record<string, string[]>) {
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={catalogs} development={false}>
        <FieldPreview type="text" problem={problem} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('the design-system field preview', () => {
  it('marks the input of a problem aria-invalid', async () => {
    const problem = { [`/values/${fields.text.name}`]: ['preview.error.field'] }
    renderPreview(problem)

    await waitFor(() => expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true'))
  })

  it('leaves the input without a problem unmarked', async () => {
    renderPreview()

    expect(screen.getByRole('textbox')).not.toHaveAttribute('aria-invalid')
  })

  it('keeps the focus on a button that was focused before the problem showed', async () => {
    const button = render(<button type="button">Before</button>).getByRole('button', { name: 'Before' })
    button.focus()
    expect(button).toHaveFocus()

    renderPreview({ [`/values/${fields.text.name}`]: ['preview.error.field'] })

    await waitFor(() => expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true'))
    expect(button).toHaveFocus()
  })
})
